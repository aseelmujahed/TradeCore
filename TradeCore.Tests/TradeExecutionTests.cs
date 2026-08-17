using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Enums;

namespace TradeCore.Tests;

public sealed class TradeExecutionTests
{
    [Fact]
    public void CreateTrade_WhenOrdersFillExactly_SetsBothOrdersToFilled()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyQuantity: 5, sellQuantity: 5);

        var trade = database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        Assert.NotNull(trade);
        Assert.Equal(0, dbContext.Orders.Single(order => order.Id == scenario.BuyOrder.Id).Quantity);
        Assert.Equal(OrderStatus.Filled, dbContext.Orders.Single(order => order.Id == scenario.BuyOrder.Id).Status);
        Assert.Equal(0, dbContext.Orders.Single(order => order.Id == scenario.SellOrder.Id).Quantity);
        Assert.Equal(OrderStatus.Filled, dbContext.Orders.Single(order => order.Id == scenario.SellOrder.Id).Status);
    }

    [Fact]
    public void CreateTrade_WhenBuyOrderIsLarger_PartiallyFillsBuyOrder()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyQuantity: 10, sellQuantity: 4);

        database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        var buy = dbContext.Orders.Single(order => order.Id == scenario.BuyOrder.Id);
        var sell = dbContext.Orders.Single(order => order.Id == scenario.SellOrder.Id);
        Assert.Equal(6, buy.Quantity);
        Assert.Equal(OrderStatus.PartiallyFilled, buy.Status);
        Assert.Equal(0, sell.Quantity);
        Assert.Equal(OrderStatus.Filled, sell.Status);
    }

    [Fact]
    public void CreateTrade_WhenSellOrderIsLarger_PartiallyFillsSellOrder()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyQuantity: 4, sellQuantity: 10, sellerShares: 10);

        database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        var buy = dbContext.Orders.Single(order => order.Id == scenario.BuyOrder.Id);
        var sell = dbContext.Orders.Single(order => order.Id == scenario.SellOrder.Id);
        Assert.Equal(OrderStatus.Filled, buy.Status);
        Assert.Equal(6, sell.Quantity);
        Assert.Equal(OrderStatus.PartiallyFilled, sell.Status);
    }

    [Fact]
    public void CreateTrade_WhenSuccessful_SettlesBalancesForMatchedQuantityOnly()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyQuantity: 10, sellQuantity: 4, buyPrice: 50m, sellPrice: 50m);

        database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        Assert.Equal(800m, dbContext.Accounts.Single(account => account.Id == scenario.Buyer.Id).Balance);
        Assert.Equal(300m, dbContext.Accounts.Single(account => account.Id == scenario.Seller.Id).Balance);
    }

    [Fact]
    public void CreateTrade_WhenBuyerHasExistingPosition_SettlesPortfolioPositions()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyerShares: 2, sellerShares: 10, buyQuantity: 4, sellQuantity: 4);

        database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        Assert.Equal(6, dbContext.PortfolioPositions.Single(position => position.AccountId == scenario.Buyer.Id).Quantity);
        Assert.Equal(6, dbContext.PortfolioPositions.Single(position => position.AccountId == scenario.Seller.Id).Quantity);
    }

    [Fact]
    public void CreateTrade_WhenBuyerHasNoPosition_CreatesBuyerPosition()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyerShares: 0, sellerShares: 10, buyQuantity: 4, sellQuantity: 4);

        database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        var position = dbContext.PortfolioPositions.Single(position => position.AccountId == scenario.Buyer.Id);
        Assert.Equal(scenario.Stock.Id, position.StockId);
        Assert.Equal(4, position.Quantity);
    }

    [Fact]
    public void CreateTrade_WhenSellerPositionReachesZero_RemovesSellerPosition()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, sellerShares: 4, buyQuantity: 4, sellQuantity: 4);

        database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        Assert.DoesNotContain(dbContext.PortfolioPositions, position => position.AccountId == scenario.Seller.Id);
    }

    [Fact]
    public void CreateTrade_WhenSuccessful_PersistsTradeWithExecutionDetails()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyQuantity: 4, sellQuantity: 4, buyPrice: 50m, sellPrice: 50m);
        var startedAt = DateTime.UtcNow;

        var created = database.CreateServices(dbContext).TradeCreationService.CreateTrade(scenario.Stock.Id);

        var trade = Assert.Single(dbContext.Trades);
        Assert.NotNull(created);
        Assert.Equal(scenario.BuyOrder.Id, trade.BuyOrderId);
        Assert.Equal(scenario.SellOrder.Id, trade.SellOrderId);
        Assert.Equal(scenario.Stock.Id, trade.StockId);
        Assert.Equal(4, trade.Quantity);
        Assert.Equal(50m, trade.Price);
        Assert.True(trade.ExecutedAt >= startedAt);
    }

    [Fact]
    public void CreateTrade_WhenBuyerHasInsufficientFunds_DoesNotModifyPersistedState()
    {
        using var database = new TradingTestDatabase();
        Guid buyerId;
        Guid sellerId;
        Guid buyOrderId;
        Guid sellOrderId;
        Guid stockId;

        using (var dbContext = database.CreateContext())
        {
            var scenario = database.SeedScenario(dbContext, buyerBalance: 100m, sellerShares: 4, buyQuantity: 4, sellQuantity: 4, buyPrice: 50m, sellPrice: 50m);
            (buyerId, sellerId, buyOrderId, sellOrderId, stockId) = (scenario.Buyer.Id, scenario.Seller.Id, scenario.BuyOrder.Id, scenario.SellOrder.Id, scenario.Stock.Id);

            Assert.Throws<InvalidOperationException>(() => database.CreateServices(dbContext).TradeCreationService.CreateTrade(stockId));
        }

        using var verificationContext = database.CreateContext();
        AssertUnchangedAfterRejectedExecution(verificationContext, buyerId, sellerId, buyOrderId, sellOrderId, stockId, 100m, 4);
    }

    [Fact]
    public void CreateTrade_WhenSellerHasInsufficientShares_DoesNotModifyPersistedState()
    {
        using var database = new TradingTestDatabase();
        Guid buyerId;
        Guid sellerId;
        Guid buyOrderId;
        Guid sellOrderId;
        Guid stockId;

        using (var dbContext = database.CreateContext())
        {
            var scenario = database.SeedScenario(dbContext, buyerBalance: 1_000m, sellerShares: 3, buyQuantity: 4, sellQuantity: 4, buyPrice: 50m, sellPrice: 50m);
            (buyerId, sellerId, buyOrderId, sellOrderId, stockId) = (scenario.Buyer.Id, scenario.Seller.Id, scenario.BuyOrder.Id, scenario.SellOrder.Id, scenario.Stock.Id);

            Assert.Throws<InvalidOperationException>(() => database.CreateServices(dbContext).TradeCreationService.CreateTrade(stockId));
        }

        using var verificationContext = database.CreateContext();
        AssertUnchangedAfterRejectedExecution(verificationContext, buyerId, sellerId, buyOrderId, sellOrderId, stockId, 1_000m, 3);
    }

    private static void AssertUnchangedAfterRejectedExecution(
        TradeCore.Console.Data.TradeCoreDbContext dbContext,
        Guid buyerId,
        Guid sellerId,
        Guid buyOrderId,
        Guid sellOrderId,
        Guid stockId,
        decimal expectedBuyerBalance,
        int expectedSellerShares)
    {
        Assert.Equal(expectedBuyerBalance, dbContext.Accounts.Single(account => account.Id == buyerId).Balance);
        Assert.Equal(100m, dbContext.Accounts.Single(account => account.Id == sellerId).Balance);
        Assert.Equal(OrderStatus.Pending, dbContext.Orders.Single(order => order.Id == buyOrderId).Status);
        Assert.Equal(4, dbContext.Orders.Single(order => order.Id == buyOrderId).Quantity);
        Assert.Equal(OrderStatus.Pending, dbContext.Orders.Single(order => order.Id == sellOrderId).Status);
        Assert.Equal(4, dbContext.Orders.Single(order => order.Id == sellOrderId).Quantity);
        Assert.Equal(expectedSellerShares, dbContext.PortfolioPositions.Single(position => position.AccountId == sellerId && position.StockId == stockId).Quantity);
        Assert.Empty(dbContext.PortfolioPositions.Where(position => position.AccountId == buyerId && position.StockId == stockId));
        Assert.Empty(dbContext.Trades);
    }
}
