using Microsoft.EntityFrameworkCore;
using TradeCore.Api.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Exceptions;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class AsyncServiceRegressionTests
{
    [Fact]
    public async Task CreateUserAsync_CreatesExactlyOneAccount_AndRejectsDuplicateEmail()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var userService = new UserService(dbContext);

        var user = await userService.CreateUserAsync("trader", "Trader@Example.Test");

        var account = await dbContext.Accounts.SingleAsync(account => account.UserId == user.Id);
        Assert.Equal($"ACC-{user.Id:N}", account.AccountNumber);
        Assert.Equal(0m, account.Balance);
        await Assert.ThrowsAsync<DuplicateUserEmailException>(
            () => userService.CreateUserAsync("another", "trader@example.test"));
        Assert.Equal(1, await dbContext.Users.CountAsync());
        Assert.Equal(1, await dbContext.Accounts.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();

        await StockDataSeeder.SeedAsync(dbContext);
        await StockDataSeeder.SeedAsync(dbContext);

        Assert.Equal(4, await dbContext.Stocks.CountAsync());
        Assert.Equal(4, await dbContext.Stocks.Select(stock => stock.Symbol).Distinct().CountAsync());
    }

    [Fact]
    public async Task CreateOrderAsync_PersistsOrder()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext);
        var orderService = new OrderService(
            dbContext,
            new AccountService(dbContext),
            new StockService(dbContext));

        var order = await orderService.CreateOrderAsync(
            scenario.Buyer.Id,
            scenario.Stock.Symbol,
            OrderType.Buy,
            2,
            50m);

        var persistedOrder = await dbContext.Orders.SingleAsync(candidate => candidate.Id == order.Id);
        Assert.Equal(OrderStatus.Pending, persistedOrder.Status);
        Assert.Equal(2, persistedOrder.Quantity);
    }
}
