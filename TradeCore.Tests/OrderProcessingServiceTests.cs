using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class OrderProcessingServiceTests
{
    [Fact]
    public async Task ProcessOrderAsync_WhenCompatibleSubmittedOrderExists_PersistsExactFillAndTrade()
    {
        using var database = new TradingTestDatabase();
        Guid stockId;
        Guid buyerId;
        Guid sellerId;
        Guid submittedOrderId;

        using (var dbContext = database.CreateContext())
        {
            var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 4, buyPrice: 1m, sellQuantity: 4);
            var submittedOrder = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);
            var result = await database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submittedOrder);

            Assert.True(result.HasTrade);
            Assert.NotNull(result.Trade);
            Assert.Equal(submittedOrder.Id, result.Trade.BuyOrderId);
            (stockId, buyerId, sellerId, submittedOrderId) = (scenario.Stock.Id, scenario.Buyer.Id, scenario.Seller.Id, submittedOrder.Id);
        }

        await using var verificationContext = database.CreateContext();
        var trade = Assert.Single(await verificationContext.Trades.ToListAsync());
        Assert.Equal(submittedOrderId, trade.BuyOrderId);
        Assert.Equal(stockId, trade.StockId);
        Assert.Equal(OrderStatus.Filled, (await verificationContext.Orders.SingleAsync(order => order.Id == submittedOrderId)).Status);
        Assert.Equal(800m, (await verificationContext.Accounts.SingleAsync(account => account.Id == buyerId)).Balance);
        Assert.Equal(300m, (await verificationContext.Accounts.SingleAsync(account => account.Id == sellerId)).Balance);
        Assert.Equal(4, (await verificationContext.PortfolioPositions.SingleAsync(position => position.AccountId == buyerId)).Quantity);
        Assert.Empty(await verificationContext.PortfolioPositions.Where(position => position.AccountId == sellerId).ToListAsync());
    }

    [Fact]
    public async Task ProcessOrderAsync_WhenNoCompatibleMatch_LeavesSubmittedOrderPending()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellPrice: 195m);
        var submittedOrder = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 190m);

        var result = await database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submittedOrder);

        Assert.False(result.HasTrade);
        Assert.Equal(OrderStatus.Pending, (await dbContext.Orders.SingleAsync(order => order.Id == submittedOrder.Id)).Status);
        Assert.Empty(await dbContext.Trades.ToListAsync());
        Assert.Equal(1_000m, (await dbContext.Accounts.SingleAsync(account => account.Id == scenario.Buyer.Id)).Balance);
        Assert.Empty(await dbContext.PortfolioPositions.Where(position => position.AccountId == scenario.Buyer.Id).ToListAsync());
    }

    [Fact]
    public async Task ProcessOrderAsync_WhenPartiallyFilled_KeepsRemainingOrderEligible()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 10, buyPrice: 1m, sellQuantity: 10);
        var firstBuy = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);
        var services = database.CreateServices(dbContext);

        await services.OrderProcessingService.ProcessOrderAsync(firstBuy);
        var secondBuy = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);
        var secondResult = await services.OrderProcessingService.ProcessOrderAsync(secondBuy);

        Assert.True(secondResult.HasTrade);
        var sellOrder = await dbContext.Orders.SingleAsync(order => order.Id == scenario.SellOrder.Id);
        Assert.Equal(OrderStatus.PartiallyFilled, sellOrder.Status);
        Assert.Equal(2, sellOrder.Quantity);
        Assert.Equal(2, await dbContext.Trades.CountAsync());
    }

    [Fact]
    public async Task ProcessOrderAsync_WhenPreviouslyFilledOrderExists_DoesNotMatchItAgain()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 4, buyPrice: 1m, sellQuantity: 4);
        var services = database.CreateServices(dbContext);

        var firstSubmittedOrder = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);
        await services.OrderProcessingService.ProcessOrderAsync(firstSubmittedOrder);
        var submittedOrder = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);
        var result = await services.OrderProcessingService.ProcessOrderAsync(submittedOrder);

        Assert.False(result.HasTrade);
        Assert.Equal(OrderStatus.Filled, (await dbContext.Orders.SingleAsync(order => order.Id == scenario.SellOrder.Id)).Status);
        Assert.Equal(OrderStatus.Pending, (await dbContext.Orders.SingleAsync(order => order.Id == submittedOrder.Id)).Status);
        Assert.Equal(1, await dbContext.Trades.CountAsync());
    }

    [Fact]
    public async Task ProcessOrderAsync_WhenBuyerHasInsufficientFunds_PreservesPersistedState()
    {
        using var database = new TradingTestDatabase();
        Guid submittedOrderId;
        Guid sellOrderId;
        Guid buyerId;
        Guid sellerId;
        Guid stockId;

        using (var dbContext = database.CreateContext())
        {
            var scenario = await database.SeedScenarioAsync(dbContext, buyerBalance: 100m, sellerShares: 4, buyPrice: 1m, sellQuantity: 4);
            var submittedOrder = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);
            (submittedOrderId, sellOrderId, buyerId, sellerId, stockId) = (submittedOrder.Id, scenario.SellOrder.Id, scenario.Buyer.Id, scenario.Seller.Id, scenario.Stock.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submittedOrder));
        }

        await using var verificationContext = database.CreateContext();
        await AssertRejectedProcessingStateAsync(verificationContext, submittedOrderId, sellOrderId, buyerId, sellerId, stockId, 100m, 4);
    }

    [Fact]
    public async Task ProcessOrderAsync_WhenSellerHasInsufficientShares_PreservesPersistedState()
    {
        using var database = new TradingTestDatabase();
        Guid submittedOrderId;
        Guid sellOrderId;
        Guid buyerId;
        Guid sellerId;
        Guid stockId;

        using (var dbContext = database.CreateContext())
        {
            var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 3, buyPrice: 1m, sellQuantity: 4);
            var submittedOrder = await CreateOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);
            (submittedOrderId, sellOrderId, buyerId, sellerId, stockId) = (submittedOrder.Id, scenario.SellOrder.Id, scenario.Buyer.Id, scenario.Seller.Id, scenario.Stock.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submittedOrder));
        }

        await using var verificationContext = database.CreateContext();
        await AssertRejectedProcessingStateAsync(verificationContext, submittedOrderId, sellOrderId, buyerId, sellerId, stockId, 1_000m, 3);
    }

    [Fact]
    public async Task ProcessOrderAsync_DoesNotMatchOrdersFromAnotherStock()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellPrice: 50m);
        var otherStock = new Stock(Guid.NewGuid(), "OTHER", "Other Stock", 50m);
        dbContext.Stocks.Add(otherStock);
        await dbContext.SaveChangesAsync();
        var submittedOrder = new Order(Guid.NewGuid(), scenario.Buyer.Id, otherStock.Id, OrderType.Buy, 4, 50m);
        dbContext.Orders.Add(submittedOrder);
        await dbContext.SaveChangesAsync();

        var result = await database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submittedOrder);

        Assert.False(result.HasTrade);
        Assert.Empty(await dbContext.Trades.ToListAsync());
        Assert.Equal(OrderStatus.Pending, (await dbContext.Orders.SingleAsync(order => order.Id == scenario.SellOrder.Id)).Status);
    }

    [Fact]
    public async Task ProcessOrderAsync_WhenCancellationRequested_PropagatesCancellation()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(
                scenario.BuyOrder,
                cancellationTokenSource.Token));
    }

    private static async Task<Order> CreateOrderAsync(TradeCore.Console.Data.TradeCoreDbContext dbContext, Guid accountId, Stock stock, int quantity, decimal price)
    {
        var orderService = new OrderService(
            dbContext,
            new AccountService(dbContext),
            new StockService(dbContext));

        return await orderService.CreateOrderAsync(accountId, stock.Symbol, OrderType.Buy, quantity, price);
    }

    private static async Task AssertRejectedProcessingStateAsync(
        TradeCore.Console.Data.TradeCoreDbContext dbContext,
        Guid submittedOrderId,
        Guid sellOrderId,
        Guid buyerId,
        Guid sellerId,
        Guid stockId,
        decimal expectedBuyerBalance,
        int expectedSellerShares)
    {
        Assert.Empty(await dbContext.Trades.ToListAsync());
        Assert.Equal(expectedBuyerBalance, (await dbContext.Accounts.SingleAsync(account => account.Id == buyerId)).Balance);
        Assert.Equal(100m, (await dbContext.Accounts.SingleAsync(account => account.Id == sellerId)).Balance);
        Assert.Equal(OrderStatus.Pending, (await dbContext.Orders.SingleAsync(order => order.Id == submittedOrderId)).Status);
        Assert.Equal(4, (await dbContext.Orders.SingleAsync(order => order.Id == submittedOrderId)).Quantity);
        Assert.Equal(OrderStatus.Pending, (await dbContext.Orders.SingleAsync(order => order.Id == sellOrderId)).Status);
        Assert.Equal(4, (await dbContext.Orders.SingleAsync(order => order.Id == sellOrderId)).Quantity);
        Assert.Equal(expectedSellerShares, (await dbContext.PortfolioPositions.SingleAsync(
            position => position.AccountId == sellerId && position.StockId == stockId)).Quantity);
        Assert.Empty(await dbContext.PortfolioPositions.Where(position => position.AccountId == buyerId && position.StockId == stockId).ToListAsync());
    }
}
