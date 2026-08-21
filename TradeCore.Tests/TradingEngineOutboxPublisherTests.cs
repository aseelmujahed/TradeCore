using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradeCore.Console.Data;
using TradeCore.Console.Models;
using TradeCore.Messaging;
using TradeCore.TradingEngine;

namespace TradeCore.Tests;

public sealed class TradingEngineOutboxPublisherTests
{
    [Fact]
    public async Task PublishPendingMessages_after_confirmation_marks_only_trading_engine_messages_published()
    {
        using var database = new TradingTestDatabase();
        await using var setupContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(setupContext);
        var eventId = Guid.NewGuid();
        var tradingMessage = AddTradeExecutedMessage(setupContext, scenario.BuyOrder.Id, eventId);
        var apiMessage = new OutboxMessage(Guid.NewGuid(), scenario.BuyOrder.Id, OutboxMessage.OrderSubmittedMessageType, "{\"orderId\":\"" + scenario.BuyOrder.Id + "\"}");
        setupContext.OutboxMessages.Add(apiMessage);
        await setupContext.SaveChangesAsync();
        var transport = new RecordingTradingEventPublisher();
        using var services = CreateServices(database, transport);
        var publisher = CreatePublisher(services);

        var outcome = await publisher.PublishPendingMessagesAsync(CancellationToken.None);

        Assert.Equal(1, outcome.MessagesRead);
        Assert.False(outcome.HadFailure);
        Assert.Equal(eventId, Assert.Single(transport.TradeEvents).EventId);
        await using var verificationContext = database.CreateContext();
        Assert.NotNull((await verificationContext.OutboxMessages.SingleAsync(message => message.Id == tradingMessage.Id)).PublishedAt);
        Assert.Null((await verificationContext.OutboxMessages.SingleAsync(message => message.Id == apiMessage.Id)).PublishedAt);
    }

    [Fact]
    public async Task PublishPendingMessages_when_broker_is_unavailable_keeps_event_intent_and_retries_with_the_same_event_id()
    {
        using var database = new TradingTestDatabase();
        await using var setupContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(setupContext);
        var eventId = Guid.NewGuid();
        var message = AddTradeExecutedMessage(setupContext, scenario.BuyOrder.Id, eventId);
        await setupContext.SaveChangesAsync();
        var transport = new RecordingTradingEventPublisher { Exception = new InvalidOperationException("Broker unavailable") };
        using var services = CreateServices(database, transport);
        var publisher = CreatePublisher(services);

        var failedOutcome = await publisher.PublishPendingMessagesAsync(CancellationToken.None);

        Assert.True(failedOutcome.HadFailure);
        await using (var verificationContext = database.CreateContext())
        {
            var pending = await verificationContext.OutboxMessages.SingleAsync(candidate => candidate.Id == message.Id);
            Assert.Null(pending.PublishedAt);
            Assert.Equal(1, pending.AttemptCount);
        }

        transport.Exception = null;
        var recoveredOutcome = await publisher.PublishPendingMessagesAsync(CancellationToken.None);

        Assert.False(recoveredOutcome.HadFailure);
        Assert.Equal([eventId, eventId], transport.TradeEvents.Select(item => item.EventId));
        await using var recoveredContext = database.CreateContext();
        Assert.NotNull((await recoveredContext.OutboxMessages.SingleAsync(candidate => candidate.Id == message.Id)).PublishedAt);
    }

    private static OutboxMessage AddTradeExecutedMessage(TradeCoreDbContext dbContext, Guid orderId, Guid eventId)
    {
        var message = new OutboxMessage(
            Guid.NewGuid(),
            orderId,
            OutboxMessage.TradingEngineOwner,
            OutboxMessage.TradeExecutedMessageType,
            JsonSerializer.Serialize(new TradeExecutedEvent(eventId, eventId, orderId, Guid.NewGuid(), Guid.NewGuid(), 1, 50m, DateTime.UtcNow), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        dbContext.OutboxMessages.Add(message);
        return message;
    }

    private static ServiceProvider CreateServices(TradingTestDatabase database, RecordingTradingEventPublisher transport)
    {
        var services = new ServiceCollection();
        services.AddScoped<TradeCoreDbContext>(_ => database.CreateContext());
        services.AddSingleton(transport);
        services.AddScoped<ITradingEventPublisher>(provider => provider.GetRequiredService<RecordingTradingEventPublisher>());
        return services.BuildServiceProvider();
    }

    private static TradingEngineOutboxPublisher CreatePublisher(ServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RabbitMqOptions { Enabled = true, RetryDelayMilliseconds = 1 }),
            NullLogger<TradingEngineOutboxPublisher>.Instance);

    private sealed class RecordingTradingEventPublisher : ITradingEventPublisher
    {
        public List<TradeExecutedEvent> TradeEvents { get; } = [];
        public List<StockPriceUpdatedEvent> StockPriceEvents { get; } = [];
        public Exception? Exception { get; set; }

        public Task PublishAsync(TradeExecutedEvent message, CancellationToken cancellationToken)
        {
            TradeEvents.Add(message);
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }

        public Task PublishAsync(StockPriceUpdatedEvent message, CancellationToken cancellationToken)
        {
            StockPriceEvents.Add(message);
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }
}
