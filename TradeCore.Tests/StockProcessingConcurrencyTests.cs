using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class StockProcessingConcurrencyTests
{
    [Fact]
    public async Task ConcurrentBuyers_ConsumeSellLiquidityOnlyOnce()
    {
        using var database = new TradingTestDatabase();
        var locks = new StockProcessingLockRegistry();
        Guid stockId;
        Guid sellerId;
        Guid firstBuyerId;
        Guid secondBuyerId;
        Order firstBuy;
        Order secondBuy;

        await using (var setup = database.CreateContext())
        {
            var scenario = await database.SeedScenarioAsync(setup, sellerShares: 5, buyPrice: 1m, sellQuantity: 5);
            var secondUser = new User(Guid.NewGuid(), "buyer-two", "buyer-two@example.test");
            var secondBuyer = new Account(Guid.NewGuid(), secondUser.Id, "BUYER-TWO", 1_000m);
            firstBuy = new Order(Guid.NewGuid(), scenario.Buyer.Id, scenario.Stock.Id, OrderType.Buy, 3, 50m);
            secondBuy = new Order(Guid.NewGuid(), secondBuyer.Id, scenario.Stock.Id, OrderType.Buy, 3, 50m);
            setup.AddRange(secondUser, secondBuyer, firstBuy, secondBuy);
            await setup.SaveChangesAsync();
            (stockId, sellerId, firstBuyerId, secondBuyerId) = (scenario.Stock.Id, scenario.Seller.Id, scenario.Buyer.Id, secondBuyer.Id);
        }

        await Task.WhenAll(ProcessAsync(database, locks, firstBuy), ProcessAsync(database, locks, secondBuy));

        await using var verification = database.CreateContext();
        var trades = await verification.Trades.Where(trade => trade.StockId == stockId).ToListAsync();
        var sellOrder = await verification.Orders.SingleAsync(order => order.AccountId == sellerId && order.Type == OrderType.Sell);
        var executedQuantity = trades.Sum(trade => trade.Quantity);

        Assert.Equal(5, executedQuantity);
        Assert.Equal(OrderStatus.Filled, sellOrder.Status);
        Assert.Equal(0, sellOrder.Quantity);
        Assert.Empty(await verification.PortfolioPositions.Where(position => position.AccountId == sellerId).ToListAsync());
        Assert.Equal(5, await verification.PortfolioPositions
            .Where(position => position.StockId == stockId && (position.AccountId == firstBuyerId || position.AccountId == secondBuyerId))
            .SumAsync(position => position.Quantity));
        Assert.Equal(1, await verification.Orders
            .Where(order => order.Id == firstBuy.Id || order.Id == secondBuy.Id)
            .SumAsync(order => order.Quantity));
        Assert.All(await verification.Accounts.ToListAsync(), account => Assert.True(account.Balance >= 0));
    }

    [Fact]
    public async Task ConcurrentSellers_ConsumeBuyLiquidityOnlyOnce()
    {
        using var database = new TradingTestDatabase();
        var locks = new StockProcessingLockRegistry();
        Guid stockId;
        Guid buyerId;
        Order firstSell;
        Order secondSell;

        await using (var setup = database.CreateContext())
        {
            var scenario = await database.SeedScenarioAsync(setup, buyerBalance: 1_000m, sellerShares: 5, buyQuantity: 5, sellQuantity: 1, sellPrice: 100m);
            var secondUser = new User(Guid.NewGuid(), "seller-two", "seller-two@example.test");
            var secondSeller = new Account(Guid.NewGuid(), secondUser.Id, "SELLER-TWO", 100m);
            firstSell = new Order(Guid.NewGuid(), scenario.Seller.Id, scenario.Stock.Id, OrderType.Sell, 5, 50m);
            secondSell = new Order(Guid.NewGuid(), secondSeller.Id, scenario.Stock.Id, OrderType.Sell, 5, 50m);
            setup.AddRange(secondUser, secondSeller, firstSell, secondSell,
                new PortfolioPosition(Guid.NewGuid(), secondSeller.Id, scenario.Stock.Id, 5, 40m));
            await setup.SaveChangesAsync();
            (stockId, buyerId) = (scenario.Stock.Id, scenario.Buyer.Id);
        }

        await Task.WhenAll(ProcessAsync(database, locks, firstSell), ProcessAsync(database, locks, secondSell));

        await using var verification = database.CreateContext();
        var trades = await verification.Trades.Where(trade => trade.StockId == stockId).ToListAsync();
        var buyOrder = await verification.Orders.SingleAsync(order => order.AccountId == buyerId && order.Type == OrderType.Buy);

        Assert.Equal(5, trades.Sum(trade => trade.Quantity));
        Assert.Equal(OrderStatus.Filled, buyOrder.Status);
        Assert.Equal(0, buyOrder.Quantity);
        Assert.All(await verification.Orders.ToListAsync(), order => Assert.True(order.Quantity >= 0));
    }

    [Fact]
    public async Task Registry_SerializesSameStock_ButAllowsDifferentStocks_AndReleasesAfterCancellation()
    {
        var locks = new StockProcessingLockRegistry();
        var firstStock = Guid.NewGuid();
        var secondStock = Guid.NewGuid();
        await using var firstLease = await locks.AcquireAsync(firstStock);

        var sameStockWaiter = locks.AcquireAsync(firstStock).AsTask();
        await Task.Yield();
        Assert.False(sameStockWaiter.IsCompleted);

        await using var differentStockLease = await locks.AcquireAsync(secondStock);
        Assert.True(sameStockWaiter.IsCompleted == false);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => locks.AcquireAsync(firstStock, cancellation.Token).AsTask());

        await firstLease.DisposeAsync();
        await using var releasedLease = await sameStockWaiter;
    }

    [Fact]
    public async Task Registry_ReleasesLockWhenProtectedOperationFails()
    {
        var locks = new StockProcessingLockRegistry();
        var stockId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var lease = await locks.AcquireAsync(stockId);
            throw new InvalidOperationException("Expected business failure.");
        });

        await using var laterLease = await locks.AcquireAsync(stockId);
    }

    private static async Task ProcessAsync(TradingTestDatabase database, StockProcessingLockRegistry locks, Order order)
    {
        await using var context = database.CreateContext();
        await database.CreateServices(context, locks).OrderProcessingService.ProcessOrderAsync(order);
    }
}
