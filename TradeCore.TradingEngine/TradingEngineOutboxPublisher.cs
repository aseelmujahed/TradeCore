using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradeCore.Console.Data;
using TradeCore.Console.Models;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

/// <summary>Publishes committed Trading Engine event outbox messages with at-least-once delivery.</summary>
public sealed class TradingEngineOutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<TradingEngineOutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 25;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!rabbitMqOptions.Value.Enabled)
        {
            logger.LogInformation("Trading Engine outbox publishing is disabled because RabbitMQ is disabled.");
            return;
        }

        var retryDelay = TimeSpan.FromMilliseconds(Math.Clamp(rabbitMqOptions.Value.RetryDelayMilliseconds, 1_000, 30_000));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await PublishPendingMessagesAsync(stoppingToken);
                await Task.Delay(outcome.HadFailure ? retryDelay : TimeSpan.FromMilliseconds(250), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Trading Engine outbox publisher could not load pending messages; it will retry.");
                await Task.Delay(retryDelay, stoppingToken);
            }
        }
    }

    public async Task<OutboxPublishOutcome> PublishPendingMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var messageIds = await dbContext.OutboxMessages
            .Where(message => message.Owner == OutboxMessage.TradingEngineOwner
                && message.PublishedAt == null)
            .OrderBy(message => message.CreatedAt)
            .Take(BatchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        var hadFailure = false;
        foreach (var messageId in messageIds)
        {
            hadFailure |= !await PublishMessageAsync(messageId, cancellationToken);
        }

        return new OutboxPublishOutcome(messageIds.Count, hadFailure);
    }

    private async Task<bool> PublishMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITradingEventPublisher>();
        var outboxMessage = await dbContext.OutboxMessages.SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);
        if (outboxMessage is null || outboxMessage.PublishedAt is not null)
        {
            return true;
        }

        try
        {
            await PublishEventAsync(publisher, outboxMessage, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            outboxMessage.RecordFailure(exception.GetType().Name);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception persistenceException)
            {
                logger.LogError(
                    persistenceException,
                    "Could not record publication failure for Trading Engine outbox message {OutboxMessageId} for order {OrderId}.",
                    outboxMessage.Id,
                    outboxMessage.OrderId);
            }

            logger.LogWarning(
                exception,
                "Trading event outbox publication failed for message {OutboxMessageId}, order {OrderId}, and type {MessageType}; it will retry.",
                outboxMessage.Id,
                outboxMessage.OrderId,
                outboxMessage.MessageType);
            return false;
        }

        try
        {
            outboxMessage.MarkPublished(DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Trading event outbox message {OutboxMessageId} for order {OrderId} and type {MessageType} was published.",
                outboxMessage.Id,
                outboxMessage.OrderId,
                outboxMessage.MessageType);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Trading event outbox message {OutboxMessageId} for order {OrderId} was published but could not be marked published; it will retry.",
                outboxMessage.Id,
                outboxMessage.OrderId);
            return false;
        }
    }

    private static Task PublishEventAsync(
        ITradingEventPublisher publisher,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken) => outboxMessage.MessageType switch
    {
        OutboxMessage.TradeExecutedMessageType => publisher.PublishAsync(
            JsonSerializer.Deserialize<TradeExecutedEvent>(outboxMessage.Payload, SerializerOptions)
                ?? throw new InvalidDataException("Trade-executed outbox payload was empty."),
            cancellationToken),
        OutboxMessage.StockPriceUpdatedMessageType => publisher.PublishAsync(
            JsonSerializer.Deserialize<StockPriceUpdatedEvent>(outboxMessage.Payload, SerializerOptions)
                ?? throw new InvalidDataException("Stock-price-updated outbox payload was empty."),
            cancellationToken),
        _ => throw new InvalidDataException($"Unexpected Trading Engine outbox message type '{outboxMessage.MessageType}'.")
    };
}

public sealed record OutboxPublishOutcome(int MessagesRead, bool HadFailure);
