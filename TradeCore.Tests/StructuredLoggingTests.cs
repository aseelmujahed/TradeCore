using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradeCore.Api.Controllers;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.Messaging;
using TradeCore.Console.Enums;
using TradeCore.Console.Services;
using TradeCore.Messaging;

namespace TradeCore.Tests;

public sealed class StructuredLoggingTests
{
    [Fact]
    public async Task CreateOrder_WhenPublished_LogsStructuredOrderSubmission()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext);
        var logger = new RecordingLogger<OrdersController>();
        var controller = new OrdersController(
            new OrderService(dbContext, new AccountService(dbContext), new StockService(dbContext)),
            new SuccessfulOrderMessagePublisher(),
            logger);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 3, 75m),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<OrderResponse>(created.Value);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        AssertProperties(
            entry,
            ("OrderId", response.Id),
            ("AccountId", scenario.Buyer.Id),
            ("StockId", scenario.Stock.Id),
            ("OrderType", OrderType.Buy),
            ("Quantity", 3),
            ("Price", 75m));
    }

    [Fact]
    public async Task ProcessOrder_WhenTradeCommits_LogsStructuredMatchSettlementAndExecutionEventsOnce()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 4, buyPrice: 1m, sellQuantity: 4);
        var submittedOrder = await new OrderService(
            dbContext,
            new AccountService(dbContext),
            new StockService(dbContext)).CreateOrderAsync(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 4, 50m);
        var logger = new RecordingLogger<OrderProcessingService>();

        var result = await database.CreateServices(dbContext, orderProcessingLogger: logger)
            .OrderProcessingService.ProcessOrderAsync(submittedOrder);

        var trade = Assert.Single(result.Trades);
        var informationEntries = logger.Entries.Where(entry => entry.Level == LogLevel.Information).ToList();
        Assert.Equal(4, informationEntries.Count);

        AssertProperties(
            Assert.Single(informationEntries, entry => entry.Properties.ContainsKey("MatchedQuantity")),
            ("BuyOrderId", submittedOrder.Id),
            ("SellOrderId", scenario.SellOrder.Id),
            ("StockId", scenario.Stock.Id),
            ("MatchedQuantity", 4),
            ("MatchPrice", 50m));
        AssertProperties(
            Assert.Single(informationEntries, entry => entry.Properties.ContainsKey("SettlementAmount")),
            ("TradeId", trade.Id),
            ("BuyerAccountId", scenario.Buyer.Id),
            ("SellerAccountId", scenario.Seller.Id),
            ("SettlementAmount", 200m));
        AssertProperties(
            Assert.Single(informationEntries, entry => entry.Properties.ContainsKey("Quantity") && entry.Properties.ContainsKey("BuyerAccountId")),
            ("TradeId", trade.Id),
            ("StockId", scenario.Stock.Id),
            ("BuyerAccountId", scenario.Buyer.Id),
            ("SellerAccountId", scenario.Seller.Id),
            ("Quantity", 4));
        AssertProperties(
            Assert.Single(informationEntries, entry => entry.Properties.ContainsKey("Price") && entry.Properties.ContainsKey("TradeId")),
            ("TradeId", trade.Id),
            ("BuyOrderId", submittedOrder.Id),
            ("SellOrderId", scenario.SellOrder.Id),
            ("StockId", scenario.Stock.Id),
            ("Quantity", 4),
            ("Price", 50m));
    }

    [Fact]
    public async Task ProcessOrder_WhenBuyerCannotSettle_LogsWarningWithoutSuccessEvents()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(
            dbContext,
            buyerBalance: 100m,
            sellerShares: 4,
            buyPrice: 1m,
            sellQuantity: 4);
        var submittedOrder = await new OrderService(
            dbContext,
            new AccountService(dbContext),
            new StockService(dbContext)).CreateOrderAsync(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 4, 50m);
        var tradeLogger = new RecordingLogger<TradeCreationService>();
        var processingLogger = new RecordingLogger<OrderProcessingService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.CreateServices(
            dbContext,
            tradeCreationLogger: tradeLogger,
            orderProcessingLogger: processingLogger).OrderProcessingService.ProcessOrderAsync(submittedOrder));

        var warning = Assert.Single(tradeLogger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        AssertProperties(
            warning,
            ("BuyerAccountId", scenario.Buyer.Id),
            ("SellerAccountId", scenario.Seller.Id),
            ("BuyOrderId", submittedOrder.Id),
            ("SellOrderId", scenario.SellOrder.Id),
            ("StockId", scenario.Stock.Id),
            ("SettlementAmount", 200m));
        Assert.Empty(processingLogger.Entries);
    }

    private static void AssertProperties(LoggedEntry entry, params (string Name, object Value)[] expectedProperties)
    {
        foreach (var (name, value) in expectedProperties)
        {
            Assert.True(entry.Properties.TryGetValue(name, out var actual), $"Missing structured property '{name}'.");
            Assert.Equal(value, actual);
        }
    }

    private sealed class SuccessfulOrderMessagePublisher : IOrderMessagePublisher
    {
        public Task PublishAsync(OrderSubmittedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LoggedEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(value => value.Key, value => value.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LoggedEntry(logLevel, properties));
        }
    }

    private sealed record LoggedEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Properties);

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose() { }
    }
}
