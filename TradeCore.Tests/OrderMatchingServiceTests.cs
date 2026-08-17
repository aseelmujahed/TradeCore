using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Tests;

public sealed class OrderMatchingServiceTests
{
    [Fact]
    public void FindBestMatch_WhenBuyPriceMeetsSellPrice_ReturnsMatch()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyPrice: 200m, sellPrice: 195m);

        var match = database.CreateServices(dbContext).MatchingService.FindBestMatch(scenario.Stock.Id);

        Assert.NotNull(match);
        Assert.Equal(scenario.BuyOrder.Id, match.BuyOrder.Id);
        Assert.Equal(scenario.SellOrder.Id, match.SellOrder.Id);
    }

    [Fact]
    public void FindBestMatch_WhenBuyPriceIsBelowSellPrice_ReturnsNull()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyPrice: 190m, sellPrice: 195m);

        var match = database.CreateServices(dbContext).MatchingService.FindBestMatch(scenario.Stock.Id);

        Assert.Null(match);
    }

    [Fact]
    public void FindBestMatch_WithMultipleOrders_SelectsHighestBuyAndLowestSell()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyPrice: 200m, sellPrice: 195m);
        var lowerBuy = new Order(Guid.NewGuid(), scenario.Buyer.Id, scenario.Stock.Id, OrderType.Buy, 4, 199m);
        var higherSell = new Order(Guid.NewGuid(), scenario.Seller.Id, scenario.Stock.Id, OrderType.Sell, 4, 196m);
        dbContext.AddRange(lowerBuy, higherSell);
        dbContext.SaveChanges();

        var match = database.CreateServices(dbContext).MatchingService.FindBestMatch(scenario.Stock.Id);

        Assert.NotNull(match);
        Assert.Equal(scenario.BuyOrder.Id, match.BuyOrder.Id);
        Assert.Equal(scenario.SellOrder.Id, match.SellOrder.Id);
    }

    [Fact]
    public void FindBestMatch_WhenPricesAreEqual_SelectsOlderOrdersFirst()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = database.SeedScenario(dbContext, buyPrice: 200m, sellPrice: 200m);
        var newerBuy = new Order(Guid.NewGuid(), scenario.Buyer.Id, scenario.Stock.Id, OrderType.Buy, 4, 200m);
        var newerSell = new Order(Guid.NewGuid(), scenario.Seller.Id, scenario.Stock.Id, OrderType.Sell, 4, 200m);
        dbContext.AddRange(newerBuy, newerSell);
        var olderTime = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var newerTime = olderTime.AddMinutes(1);
        dbContext.Entry(scenario.BuyOrder).Property(nameof(Order.CreatedAt)).CurrentValue = olderTime;
        dbContext.Entry(scenario.SellOrder).Property(nameof(Order.CreatedAt)).CurrentValue = olderTime;
        dbContext.Entry(newerBuy).Property(nameof(Order.CreatedAt)).CurrentValue = newerTime;
        dbContext.Entry(newerSell).Property(nameof(Order.CreatedAt)).CurrentValue = newerTime;
        dbContext.SaveChanges();

        var match = database.CreateServices(dbContext).MatchingService.FindBestMatch(scenario.Stock.Id);

        Assert.NotNull(match);
        Assert.Equal(scenario.BuyOrder.Id, match.BuyOrder.Id);
        Assert.Equal(scenario.SellOrder.Id, match.SellOrder.Id);
    }
}
