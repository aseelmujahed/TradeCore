using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradeCore.Console.Data;
using TradeCore.Console.Models;
using TradeCore.Messaging;

namespace TradeCore.Api.Messaging;

/// <summary>Publishes durable messages created by the API after their order transaction has committed.</summary>
public sealed class ApiOutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<ApiOutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 25;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!rabbitMqOptions.Value.Enabled)
        {
            logger.LogInformation("API outbox publishing is disabled because RabbitMQ is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await PublishPendingMessagesAsync(stoppingToken);
                await Task.Delay(outcome.HadFailure ? FailureDelay : IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "API outbox publisher failed while loading pending messages; it will retry.");
                await Task.Delay(FailureDelay, stoppingToken);
            }
        }
    }

    public async Task<OutboxPublishOutcome> PublishPendingMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var messageIds = await dbContext.OutboxMessages
            .Where(message => message.Owner == OutboxMessage.ApiOwner
                && message.MessageType == OutboxMessage.OrderSubmittedMessageType
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
        var publisher = scope.ServiceProvider.GetRequiredService<IOrderMessagePublisher>();
        var outboxMessage = await dbContext.OutboxMessages.SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);
        if (outboxMessage is null || outboxMessage.PublishedAt is not null)
        {
            return true;
        }

        OrderSubmittedMessage message;
        try
        {
            message = JsonSerializer.Deserialize<OrderSubmittedMessage>(outboxMessage.Payload, SerializerOptions)
                ?? throw new InvalidDataException("Order-submitted outbox payload was empty.");
            if (message.OrderId != outboxMessage.OrderId)
            {
                throw new InvalidDataException("Order-submitted outbox payload did not match its order ID.");
            }

            await publisher.PublishAsync(message, cancellationToken);
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
                    "Could not record publication failure for outbox message {OutboxMessageId} for order {OrderId}.",
                    outboxMessage.Id,
                    outboxMessage.OrderId);
            }

            logger.LogWarning(
                exception,
                "Outbox publication failed for message {OutboxMessageId}, order {OrderId}, and type {MessageType}; it will retry.",
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
                "Outbox message {OutboxMessageId} for order {OrderId} and type {MessageType} was published.",
                outboxMessage.Id,
                outboxMessage.OrderId,
                outboxMessage.MessageType);
            return true;
        }
        catch (Exception exception)
        {
            // The broker confirmed the message, but the durable acknowledgement did not complete.
            // Leaving the row unpublished deliberately permits a safe, at-least-once retry.
            logger.LogWarning(
                exception,
                "Outbox message {OutboxMessageId} for order {OrderId} was published but could not be marked published; it will retry.",
                outboxMessage.Id,
                outboxMessage.OrderId);
            return false;
        }
    }
}

public sealed record OutboxPublishOutcome(int MessagesRead, bool HadFailure);
