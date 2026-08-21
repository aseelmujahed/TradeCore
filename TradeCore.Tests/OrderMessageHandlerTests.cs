using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeCore.Console.Data;
using TradeCore.Console.Services;
using TradeCore.Console.Models;
using TradeCore.Messaging;
using TradeCore.TradingEngine;

namespace TradeCore.Tests;

public sealed class OrderMessageHandlerTests
{
    [Fact]
    public async Task ProcessAsync_valid_persisted_order_uses_existing_processing_service_and_settles_trade()
    {
        using var database = new TradingTestDatabase();
        await using var setupContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(setupContext, sellerShares: 4, buyQuantity: 4, sellQuantity: 4);
        var scopes = new TestScopeFactory(database, new StockProcessingLockRegistry());
        var handler = CreateHandler(scopes);

        await handler.ProcessAsync(Serialize(scenario.BuyOrder.Id), CancellationToken.None);
        Assert.Equal(1, scopes.CreatedScopes);
        await using var verificationContext = database.CreateContext();
        Assert.Single(verificationContext.Trades);
        Assert.Equal(0, verificationContext.Orders.Single(order => order.Id == scenario.BuyOrder.Id).Quantity);
        Assert.Equal(800m, verificationContext.Accounts.Single(account => account.Id == scenario.Buyer.Id).Balance);
        var tradeEvent = JsonSerializer.Deserialize<TradeExecutedEvent>(Assert.Single(
            verificationContext.OutboxMessages.Where(message => message.MessageType == OutboxMessage.TradeExecutedMessageType)).Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(tradeEvent);
        Assert.Equal(tradeEvent.EventId, tradeEvent.TradeId);
        Assert.Equal(scenario.BuyOrder.Id, tradeEvent.BuyOrderId);
        Assert.Equal(scenario.SellOrder.Id, tradeEvent.SellOrderId);
        Assert.Equal(scenario.Stock.Id, tradeEvent.StockId);
        Assert.Equal(4, tradeEvent.Quantity);
        Assert.Equal(50m, tradeEvent.Price);
        var priceEvent = JsonSerializer.Deserialize<StockPriceUpdatedEvent>(Assert.Single(
            verificationContext.OutboxMessages.Where(message => message.MessageType == OutboxMessage.StockPriceUpdatedMessageType)).Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(priceEvent);
        Assert.Equal(tradeEvent.TradeId, priceEvent.EventId);
        Assert.Equal(scenario.Stock.Id, priceEvent.StockId);
        Assert.Equal(scenario.Stock.Symbol, priceEvent.Symbol);
        Assert.Equal(50m, priceEvent.Price);
    }

    [Fact]
    public async Task ProcessAsync_uses_a_new_scope_for_each_received_message()
    {
        using var database = new TradingTestDatabase();
        await using var setupContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(setupContext, sellerShares: 4, buyQuantity: 4, sellQuantity: 4);
        var scopes = new TestScopeFactory(database, new StockProcessingLockRegistry());
        var handler = CreateHandler(scopes);

        await handler.ProcessAsync(Serialize(scenario.BuyOrder.Id), CancellationToken.None);
        await handler.ProcessAsync(Serialize(scenario.BuyOrder.Id), CancellationToken.None);

        Assert.Equal(2, scopes.CreatedScopes);
        Assert.Equal(2, scopes.DisposedScopes);
    }

    [Fact]
    public async Task ProcessAsync_redelivered_filled_order_does_not_duplicate_trade_or_settlement()
    {
        using var database = new TradingTestDatabase();
        await using var setupContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(setupContext, sellerShares: 4, buyQuantity: 4, sellQuantity: 4);
        var handler = CreateHandler(new TestScopeFactory(database, new StockProcessingLockRegistry()));

        await handler.ProcessAsync(Serialize(scenario.BuyOrder.Id), CancellationToken.None);
        await handler.ProcessAsync(Serialize(scenario.BuyOrder.Id), CancellationToken.None);

        await using var verificationContext = database.CreateContext();
        var trade = Assert.Single(await verificationContext.Trades.ToListAsync());
        Assert.Equal(scenario.BuyOrder.Id, trade.BuyOrderId);
        Assert.Equal(800m, (await verificationContext.Accounts.SingleAsync(account => account.Id == scenario.Buyer.Id)).Balance);
        Assert.Equal(300m, (await verificationContext.Accounts.SingleAsync(account => account.Id == scenario.Seller.Id)).Balance);
        Assert.Equal(4, (await verificationContext.PortfolioPositions.SingleAsync(position => position.AccountId == scenario.Buyer.Id)).Quantity);
        Assert.Empty(await verificationContext.PortfolioPositions.Where(position => position.AccountId == scenario.Seller.Id).ToListAsync());
        Assert.Equal(0, (await verificationContext.Orders.SingleAsync(order => order.Id == scenario.BuyOrder.Id)).Quantity);
        Assert.NotNull((await verificationContext.Orders.SingleAsync(order => order.Id == scenario.BuyOrder.Id)).SubmittedMessageProcessedAt);
        Assert.Equal(2, await verificationContext.OutboxMessages.CountAsync(message => message.Owner == OutboxMessage.TradingEngineOwner));
    }

    [Fact]
    public async Task ProcessAsync_malformed_message_is_rejected_without_creating_a_scope()
    {
        using var database = new TradingTestDatabase();
        var scopes = new TestScopeFactory(database, new StockProcessingLockRegistry());
        var handler = CreateHandler(scopes);

        await Assert.ThrowsAsync<InvalidOrderMessageException>(() => handler.ProcessAsync("not-json"u8.ToArray(), CancellationToken.None));
        Assert.Equal(0, scopes.CreatedScopes);
    }

    [Fact]
    public async Task ProcessAsync_empty_message_is_rejected_without_creating_a_scope()
    {
        using var database = new TradingTestDatabase();
        var scopes = new TestScopeFactory(database, new StockProcessingLockRegistry());
        var handler = CreateHandler(scopes);

        await Assert.ThrowsAsync<InvalidOrderMessageException>(() => handler.ProcessAsync(Array.Empty<byte>(), CancellationToken.None));

        Assert.Equal(0, scopes.CreatedScopes);
    }

    [Fact]
    public async Task ProcessAsync_missing_order_is_not_reconstructed_or_processed()
    {
        using var database = new TradingTestDatabase();
        var scopes = new TestScopeFactory(database, new StockProcessingLockRegistry());
        var handler = CreateHandler(scopes);

        await Assert.ThrowsAsync<PersistedOrderNotFoundException>(() => handler.ProcessAsync(Serialize(Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(1, scopes.CreatedScopes);
        await using var verificationContext = database.CreateContext();
        Assert.Empty(verificationContext.Orders);
        Assert.Empty(verificationContext.Trades);
    }

    private static OrderMessageHandler CreateHandler(IServiceScopeFactory scopes) => new(scopes);

    private static byte[] Serialize(Guid orderId) =>
        JsonSerializer.SerializeToUtf8Bytes(new OrderSubmittedMessage(orderId), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class TestScopeFactory(TradingTestDatabase database, StockProcessingLockRegistry locks) : IServiceScopeFactory
    {
        public int CreatedScopes { get; private set; }
        public int DisposedScopes { get; private set; }

        public IServiceScope CreateScope()
        {
            CreatedScopes++;
            var dbContext = database.CreateContext();
            var services = database.CreateServices(dbContext, locks);
            return new TestScope(
                new TestServiceProvider(dbContext, services.OrderProcessingService),
                () => DisposedScopes++);
        }
    }

    private sealed class TestScope(IServiceProvider serviceProvider, Action onDispose) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public void Dispose()
        {
            if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            onDispose();
        }
    }

    private sealed class TestServiceProvider(TradeCoreDbContext dbContext, OrderProcessingService orderProcessingService)
        : IServiceProvider, IDisposable
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TradeCoreDbContext)
            ? dbContext
            : serviceType == typeof(OrderProcessingService)
                ? orderProcessingService
                : null;

        public void Dispose() => dbContext.Dispose();
    }

}
