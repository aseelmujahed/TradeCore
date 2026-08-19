using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TradeCore.Console.Data;
using TradeCore.Console.Services;
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
