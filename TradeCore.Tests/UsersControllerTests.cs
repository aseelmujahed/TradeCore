using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.Controllers;
using TradeCore.Api.DTOs.Accounts;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class UsersControllerTests
{
    [Fact]
    public async Task GetAccount_WhenUserHasAccount_ReturnsAccountResponseForRequestedUser()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var userService = new UserService(dbContext);
        var accountService = new AccountService(dbContext);
        var user = await userService.CreateUserAsync("trader", "trader@example.test");
        var account = await accountService.GetAccountByUserIdAsync(user.Id);
        Assert.NotNull(account);
        await accountService.DepositAsync(account.Id, 10_000m);
        var controller = CreateController(dbContext);

        var result = await controller.GetAccount(user.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AccountResponse>(okResult.Value);
        Assert.Equal(account.Id, response.Id);
        Assert.Equal(user.Id, response.UserId);
        Assert.Equal(account.AccountNumber, response.AccountNumber);
        Assert.Equal(10_000m, response.Balance);
        Assert.IsNotType<Account>(response);
    }

    [Fact]
    public async Task GetAccount_WhenUserDoesNotExist_ReturnsNotFound()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var controller = CreateController(dbContext);

        var result = await controller.GetAccount(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAccount_WhenUserHasNoAccount_ReturnsNotFound()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var user = new TradeCore.Console.Models.User(Guid.NewGuid(), "no-account", "no-account@example.test");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.GetAccount(user.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    private static UsersController CreateController(TradeCore.Console.Data.TradeCoreDbContext dbContext)
    {
        var accountService = new AccountService(dbContext);
        var stockService = new StockService(dbContext);
        return new UsersController(
            new UserService(dbContext),
            accountService,
            new PortfolioService(dbContext, accountService, stockService));
    }
}
