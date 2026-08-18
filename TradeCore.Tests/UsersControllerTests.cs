using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.Controllers;
using TradeCore.Api.DTOs.Accounts;
using System.ComponentModel.DataAnnotations;
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

    [Fact]
    public async Task Deposit_WhenValid_PersistsUpdatedBalanceAndPreservesAccountIdentity()
    {
        using var database = new TradingTestDatabase();
        Guid userId;
        Guid accountId;
        string accountNumber;

        using (var dbContext = database.CreateContext())
        {
            var user = await new UserService(dbContext).CreateUserAsync("depositor", "depositor@example.test");
            var account = await new AccountService(dbContext).GetAccountByUserIdAsync(user.Id);
            Assert.NotNull(account);
            (userId, accountId, accountNumber) = (user.Id, account.Id, account.AccountNumber);

            var result = await CreateController(dbContext).Deposit(userId, new DepositRequest { Amount = 10_000m }, CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<AccountResponse>(okResult.Value);
            Assert.Equal(10_000m, response.Balance);
            Assert.Equal(accountId, response.Id);
            Assert.Equal(userId, response.UserId);
            Assert.Equal(accountNumber, response.AccountNumber);
        }

        await using var verificationContext = database.CreateContext();
        var persistedAccount = await new AccountService(verificationContext).GetAccountByUserIdAsync(userId);
        Assert.NotNull(persistedAccount);
        Assert.Equal(accountId, persistedAccount.Id);
        Assert.Equal(accountNumber, persistedAccount.AccountNumber);
        Assert.Equal(10_000m, persistedAccount.Balance);
        var getResult = await CreateController(verificationContext).GetAccount(userId, CancellationToken.None);
        var getOkResult = Assert.IsType<OkObjectResult>(getResult.Result);
        Assert.Equal(10_000m, Assert.IsType<AccountResponse>(getOkResult.Value).Balance);
    }

    [Fact]
    public async Task Deposit_WhenAccountHasBalance_AccumulatesMultipleDeposits()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var user = await new UserService(dbContext).CreateUserAsync("accumulator", "accumulator@example.test");
        var controller = CreateController(dbContext);

        await controller.Deposit(user.Id, new DepositRequest { Amount = 10_000m }, CancellationToken.None);
        var result = await controller.Deposit(user.Id, new DepositRequest { Amount = 500m }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(10_500m, Assert.IsType<AccountResponse>(okResult.Value).Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task Deposit_WhenAmountIsNotPositive_ReturnsBadRequest(decimal amount)
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var user = await new UserService(dbContext).CreateUserAsync("invalid-deposit", "invalid-deposit@example.test");

        var result = await CreateController(dbContext).Deposit(user.Id, new DepositRequest { Amount = amount }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Deposit_WhenUserDoesNotExist_ReturnsNotFound()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();

        var result = await CreateController(dbContext).Deposit(
            Guid.NewGuid(),
            new DepositRequest { Amount = 100m },
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Deposit_WhenUserHasNoAccount_ReturnsNotFound()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var user = new TradeCore.Console.Models.User(Guid.NewGuid(), "no-account-deposit", "no-account-deposit@example.test");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var result = await CreateController(dbContext).Deposit(user.Id, new DepositRequest { Amount = 100m }, CancellationToken.None);

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
