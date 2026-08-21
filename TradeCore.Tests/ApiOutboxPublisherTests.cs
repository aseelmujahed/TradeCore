using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradeCore.Api.Messaging;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;
using TradeCore.Messaging;

namespace TradeCore.Tests;

public sealed class ApiOutboxPublisherTests
{
    [Fact]
    public async Task PublishPendingMessages_after_confirmation_marks_the_api_outbox_message_published()
    {
        using var database = new TradingTestDatabase();
        await using var setupContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(setupContext);
        var order = await CreateOrderWithOutboxAsync(setupContext, scenario);
        var transport = new RecordingOrderMessagePublisher();
        using var services = CreateServices(database, transport);
        var publisher = CreatePublisher(services);

        var outcome = await publisher.PublishPendingMessagesAsync(CancellationToken.None);

        Assert.Equal(1, outcome.MessagesRead);
        Assert.False(outcome.HadFailure);
        Assert.Equal(order.Id, Assert.Single(transport.Messages).OrderId);
        await using var verificationContext = database.CreateContext();
        var outboxMessage = await verificationContext.OutboxMessages.SingleAsync(message => message.OrderId == order.Id);
        Assert.NotNull(outboxMessage.PublishedAt);
        Assert.Equal(0, outboxMessage.AttemptCount);
    }

    [Fact]
    public async Task PublishPendingMessages_when_confirmation_fails_keeps_the_message_unpublished_and_retries_when_recovered()
    {
        using var database = new TradingTestDatabase();
        await using var setupContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(setupContext);
        var order = await CreateOrderWithOutboxAsync(setupContext, scenario);
        var transport = new RecordingOrderMessagePublisher { Exception = new InvalidOperationException("Broker unavailable") };
        using var services = CreateServices(database, transport);
        var publisher = CreatePublisher(services);

        var failedOutcome = await publisher.PublishPendingMessagesAsync(CancellationToken.None);

        Assert.True(failedOutcome.HadFailure);
        await using (var verificationContext = database.CreateContext())
        {
            var pending = await verificationContext.OutboxMessages.SingleAsync(message => message.OrderId == order.Id);
            Assert.Null(pending.PublishedAt);
            Assert.Equal(1, pending.AttemptCount);
        }

        transport.Exception = null;
        var recoveredOutcome = await publisher.PublishPendingMessagesAsync(CancellationToken.None);

        Assert.False(recoveredOutcome.HadFailure);
        Assert.Equal(2, transport.Messages.Count);
        await using var recoveredContext = database.CreateContext();
        Assert.NotNull((await recoveredContext.OutboxMessages.SingleAsync(message => message.OrderId == order.Id)).PublishedAt);
    }

    private static async Task<Order> CreateOrderWithOutboxAsync(TradeCoreDbContext dbContext, TradingScenario scenario) =>
        (await new OrderService(
            dbContext,
            new AccountService(dbContext),
            new StockService(dbContext)).CreateOrderWithOutboxAsync(
            scenario.Buyer.Id,
            scenario.Stock.Symbol,
            OrderType.Buy,
            2,
            50m)).Order;

    private static ServiceProvider CreateServices(TradingTestDatabase database, RecordingOrderMessagePublisher transport)
    {
        var services = new ServiceCollection();
        services.AddScoped<TradeCoreDbContext>(_ => database.CreateContext());
        services.AddSingleton(transport);
        services.AddScoped<IOrderMessagePublisher>(provider => provider.GetRequiredService<RecordingOrderMessagePublisher>());
        return services.BuildServiceProvider();
    }

    private static ApiOutboxPublisher CreatePublisher(ServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RabbitMqOptions { Enabled = true }),
            NullLogger<ApiOutboxPublisher>.Instance);

    private sealed class RecordingOrderMessagePublisher : IOrderMessagePublisher
    {
        public List<OrderSubmittedMessage> Messages { get; } = [];
        public Exception? Exception { get; set; }

        public Task PublishAsync(OrderSubmittedMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }
}
