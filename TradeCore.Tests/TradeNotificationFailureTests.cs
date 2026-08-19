using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCore.Api.Controllers;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.DTOs.Trades;
using TradeCore.Api.Notifications;
using TradeCore.Console.Enums;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class TradeNotificationFailureTests
{
    [Fact]
    public async Task CreateOrder_WhenSignalRNotificationFails_ReturnsCreatedAndKeepsCommittedTrade()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 4, buyPrice: 1m, sellQuantity: 4);
        var notifier = new ThrowingSignalRTradeExecutionNotifier();
        var controller = CreateController(dbContext, notifier);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 4, 50m),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.NotNull(created.Value);
        Assert.Equal(1, notifier.Attempts);
        var trade = Assert.Single(dbContext.Trades);
        Assert.Equal(scenario.Stock.Id, trade.StockId);
        Assert.Equal(4, trade.Quantity);
        Assert.Equal(50m, trade.Price);
        Assert.Equal(1, dbContext.Trades.Count());
    }

    [Fact]
    public async Task CreateOrder_WhenStockPriceSignalRNotificationFails_ReturnsCreatedAndKeepsCommittedStockPrice()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 4, sellPrice: 45m, buyPrice: 1m, sellQuantity: 4);
        var notifier = new ThrowingStockPriceNotifier();
        var controller = CreateController(dbContext, new NoOpTradeExecutionNotifier(), notifier);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 4, 50m),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(1, notifier.Attempts);
        Assert.Single(dbContext.Trades);
        Assert.Equal(45m, dbContext.Stocks.Single(stock => stock.Id == scenario.Stock.Id).CurrentPrice);
    }

    private static OrdersController CreateController(
        TradeCore.Console.Data.TradeCoreDbContext dbContext,
        ITradeExecutionNotifier notifier,
        IStockPriceNotifier? stockPriceNotifier = null)
    {
        var accountService = new AccountService(dbContext);
        var stockService = new StockService(dbContext);
        var portfolioService = new PortfolioService(dbContext, accountService, stockService);
        var tradeCreationService = new TradeCreationService(
            dbContext,
            new OrderMatchingService(new OrderBookService(dbContext)),
            portfolioService);
        return new OrdersController(
            new OrderService(dbContext, accountService, stockService),
            new OrderProcessingService(dbContext, tradeCreationService),
            notifier,
            stockPriceNotifier ?? new NoOpStockPriceNotifier(),
            NullLogger<OrdersController>.Instance);
    }

    private sealed class ThrowingSignalRTradeExecutionNotifier : ITradeExecutionNotifier
    {
        public int Attempts { get; private set; }

        public Task NotifyTradeExecutedAsync(TradeResponse trade, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("Simulated SignalR broadcast failure.");
        }
    }

    private sealed class NoOpStockPriceNotifier : IStockPriceNotifier
    {
        public Task NotifyStockPriceUpdatedAsync(TradeCore.Api.DTOs.Stocks.StockPriceUpdatedResponse update, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpTradeExecutionNotifier : ITradeExecutionNotifier
    {
        public Task NotifyTradeExecutedAsync(TradeResponse trade, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingStockPriceNotifier : IStockPriceNotifier
    {
        public int Attempts { get; private set; }

        public Task NotifyStockPriceUpdatedAsync(TradeCore.Api.DTOs.Stocks.StockPriceUpdatedResponse update, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("Simulated SignalR stock-price broadcast failure.");
        }
    }
}
