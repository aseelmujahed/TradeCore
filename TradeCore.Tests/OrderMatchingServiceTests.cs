using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using Microsoft.EntityFrameworkCore;

namespace TradeCore.Tests;

public sealed class OrderMatchingServiceTests
{
    [Fact]
    public async Task FindBestMatch_WhenBuyPriceMeetsSellPrice_ReturnsMatch()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, buyPrice: 200m, sellPrice: 195m);

        var match = await database.CreateServices(dbContext).MatchingService.FindBestMatchAsync(scenario.Stock.Id);

        Assert.NotNull(match);
        Assert.Equal(scenario.BuyOrder.Id, match.BuyOrder.Id);
        Assert.Equal(scenario.SellOrder.Id, match.SellOrder.Id);
    }

    [Fact]
    public async Task FindBestMatch_WhenBuyPriceIsBelowSellPrice_ReturnsNull()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, buyPrice: 190m, sellPrice: 195m);

        var match = await database.CreateServices(dbContext).MatchingService.FindBestMatchAsync(scenario.Stock.Id);

        Assert.Null(match);
    }

    [Fact]
    public async Task FindBestMatch_WithMultipleOrders_SelectsHighestBuyAndLowestSell()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, buyPrice: 200m, sellPrice: 195m);
        var lowerBuy = new Order(Guid.NewGuid(), scenario.Buyer.Id, scenario.Stock.Id, OrderType.Buy, 4, 199m);
        var higherSell = new Order(Guid.NewGuid(), scenario.Seller.Id, scenario.Stock.Id, OrderType.Sell, 4, 196m);
        dbContext.AddRange(lowerBuy, higherSell);
        await dbContext.SaveChangesAsync();

        var match = await database.CreateServices(dbContext).MatchingService.FindBestMatchAsync(scenario.Stock.Id);

        Assert.NotNull(match);
        Assert.Equal(scenario.BuyOrder.Id, match.BuyOrder.Id);
        Assert.Equal(scenario.SellOrder.Id, match.SellOrder.Id);
    }

    [Fact]
    public async Task FindBestMatch_WhenPricesAreEqual_SelectsOlderOrdersFirst()
    {
        using var database = new TradingTestDatabase();
        using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, buyPrice: 200m, sellPrice: 200m);
        var newerBuy = new Order(Guid.NewGuid(), scenario.Buyer.Id, scenario.Stock.Id, OrderType.Buy, 4, 200m);
        var newerSell = new Order(Guid.NewGuid(), scenario.Seller.Id, scenario.Stock.Id, OrderType.Sell, 4, 200m);
        dbContext.AddRange(newerBuy, newerSell);
        var olderTime = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var newerTime = olderTime.AddMinutes(1);
        dbContext.Entry(scenario.BuyOrder).Property(nameof(Order.CreatedAt)).CurrentValue = olderTime;
        dbContext.Entry(scenario.SellOrder).Property(nameof(Order.CreatedAt)).CurrentValue = olderTime;
        dbContext.Entry(newerBuy).Property(nameof(Order.CreatedAt)).CurrentValue = newerTime;
        dbContext.Entry(newerSell).Property(nameof(Order.CreatedAt)).CurrentValue = newerTime;
        await dbContext.SaveChangesAsync();

        var match = await database.CreateServices(dbContext).MatchingService.FindBestMatchAsync(scenario.Stock.Id);

        Assert.NotNull(match);
        Assert.Equal(scenario.BuyOrder.Id, match.BuyOrder.Id);
        Assert.Equal(scenario.SellOrder.Id, match.SellOrder.Id);
    }

    [Fact]
    public async Task FindBestMatch_WhenOnlyCompatibleOrdersBelongToSameAccount_ReturnsNull()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, buyPrice: 450m, sellPrice: 500m);
        var selfSell = new Order(Guid.NewGuid(), scenario.Buyer.Id, scenario.Stock.Id, OrderType.Sell, 2, 440m);
        dbContext.Orders.Add(selfSell);
        await dbContext.SaveChangesAsync();

        var match = await database.CreateServices(dbContext).MatchingService.FindBestMatchAsync(scenario.Stock.Id);

        Assert.Null(match);
    }

    [Fact]
    public async Task FindBestMatch_SkipsSelfOwnedBestSell_AndSelectsNextEligibleCounterparty()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, buyPrice: 450m, sellPrice: 445m);
        var selfSell = new Order(Guid.NewGuid(), scenario.Buyer.Id, scenario.Stock.Id, OrderType.Sell, 2, 440m);
        dbContext.Orders.Add(selfSell);
        await dbContext.SaveChangesAsync();

        var match = await database.CreateServices(dbContext).MatchingService.FindBestMatchAsync(scenario.Stock.Id);

        Assert.NotNull(match);
        Assert.Equal(scenario.BuyOrder.Id, match.BuyOrder.Id);
        Assert.Equal(scenario.SellOrder.Id, match.SellOrder.Id);
        Assert.NotEqual(match.BuyOrder.AccountId, match.SellOrder.AccountId);
    }
}
