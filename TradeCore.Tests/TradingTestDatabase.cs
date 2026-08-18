using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class TradingTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<TradeCoreDbContext> _options;

    public TradingTestDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<TradeCoreDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;

        using var dbContext = CreateContext();
        dbContext.Database.EnsureCreated();
    }

    public TradeCoreDbContext CreateContext() => new(_options);

    public TradingServices CreateServices(
        TradeCoreDbContext dbContext,
        StockProcessingLockRegistry? stockProcessingLocks = null)
    {
        var accountService = new AccountService(dbContext);
        var stockService = new StockService(dbContext);
        var portfolioService = new PortfolioService(dbContext, accountService, stockService);
        var orderBookService = new OrderBookService(dbContext);
        var matchingService = new OrderMatchingService(orderBookService);
        var tradeCreationService = new TradeCreationService(dbContext, matchingService, portfolioService);

        return new TradingServices(
            matchingService,
            tradeCreationService,
            new OrderProcessingService(dbContext, tradeCreationService, stockProcessingLocks ?? new StockProcessingLockRegistry()));
    }

    public async Task<TradingScenario> SeedScenarioAsync(
        TradeCoreDbContext dbContext,
        decimal buyerBalance = 1_000m,
        int sellerShares = 10,
        int buyQuantity = 4,
        decimal buyPrice = 50m,
        int sellQuantity = 4,
        decimal sellPrice = 50m,
        int buyerShares = 0)
    {
        var stock = new Stock(Guid.NewGuid(), "TC", "TradeCore", 50m);
        var buyerUser = new User(Guid.NewGuid(), "buyer", "buyer@example.test");
        var sellerUser = new User(Guid.NewGuid(), "seller", "seller@example.test");
        var buyer = new Account(Guid.NewGuid(), buyerUser.Id, "BUYER", buyerBalance);
        var seller = new Account(Guid.NewGuid(), sellerUser.Id, "SELLER", 100m);
        var buyOrder = new Order(Guid.NewGuid(), buyer.Id, stock.Id, OrderType.Buy, buyQuantity, buyPrice);
        var sellOrder = new Order(Guid.NewGuid(), seller.Id, stock.Id, OrderType.Sell, sellQuantity, sellPrice);

        dbContext.AddRange(stock, buyerUser, sellerUser, buyer, seller, buyOrder, sellOrder);
        if (buyerShares > 0)
        {
            dbContext.PortfolioPositions.Add(new PortfolioPosition(Guid.NewGuid(), buyer.Id, stock.Id, buyerShares, 40m));
        }

        if (sellerShares > 0)
        {
            dbContext.PortfolioPositions.Add(new PortfolioPosition(Guid.NewGuid(), seller.Id, stock.Id, sellerShares, 40m));
        }

        await dbContext.SaveChangesAsync();
        return new TradingScenario(stock, buyer, seller, buyOrder, sellOrder);
    }

    public void Dispose() => _connection.Dispose();
}

public sealed record TradingServices(
    OrderMatchingService MatchingService,
    TradeCreationService TradeCreationService,
    OrderProcessingService OrderProcessingService);

public sealed record TradingScenario(
    Stock Stock,
    Account Buyer,
    Account Seller,
    Order BuyOrder,
    Order SellOrder);
