using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeCore.Api.DTOs.Accounts;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.DTOs.Portfolio;
using TradeCore.Api.DTOs.Users;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Tests;

public sealed class ApiIntegrationTests : IClassFixture<TradeCoreApiFactory>
{
    private readonly TradeCoreApiFactory _factory;

    public ApiIntegrationTests(TradeCoreApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateUser_ValidRequest_ReturnsAndPersistsCreatedUser()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var request = new { username = $"api-user-{suffix}", email = $"api-user-{suffix}@example.test" };
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(request.username, created.Username);
        Assert.Equal(request.email, created.Email);

        var getResponse = await client.GetAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(created, await getResponse.Content.ReadFromJsonAsync<UserResponse>());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var persisted = await dbContext.Users.SingleAsync(user => user.Id == created.Id);
        Assert.Equal(request.username, persisted.Username);
        Assert.Equal(request.email, persisted.Email);
    }

    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsPendingOrderAndPersistsIt()
    {
        var (user, account) = await CreateUserAndGetAccountAsync();
        using var client = _factory.CreateClient();
        var request = new { accountId = account.Id, stockSymbol = "AAPL", orderType = "Buy", quantity = 3, price = 181.25m };

        var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(account.Id, created.AccountId);
        Assert.Equal(OrderType.Buy, created.Type);
        Assert.Equal(3, created.Quantity);
        Assert.Equal(181.25m, created.Price);
        Assert.Equal(OrderStatus.Pending, created.Status);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var stock = await dbContext.Stocks.SingleAsync(stock => stock.Symbol == "AAPL");
        var persisted = await dbContext.Orders.SingleAsync(order => order.Id == created.Id);
        Assert.Equal(user.Id, (await dbContext.Accounts.SingleAsync(accountEntity => accountEntity.Id == persisted.AccountId)).UserId);
        Assert.Equal(stock.Id, created.StockId);
        Assert.Equal(created.AccountId, persisted.AccountId);
        Assert.Equal(created.StockId, persisted.StockId);
        Assert.Equal(created.Type, persisted.Type);
        Assert.Equal(created.Quantity, persisted.Quantity);
        Assert.Equal(created.Price, persisted.Price);
        Assert.Equal(OrderStatus.Pending, persisted.Status);
    }

    [Fact]
    public async Task GetOrderById_OrderCreatedThroughApi_ReturnsItsPersistedData()
    {
        var (_, account) = await CreateUserAndGetAccountAsync();
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            accountId = account.Id, stockSymbol = "MSFT", orderType = "Sell", quantity = 2, price = 421m
        });
        var created = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/orders/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retrieved = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(created, retrieved);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var persisted = await dbContext.Orders.SingleAsync(order => order.Id == created.Id);
        Assert.Equal(retrieved, new OrderResponse(persisted.Id, persisted.AccountId, persisted.StockId, persisted.Type, persisted.Quantity, persisted.Price, persisted.Status, persisted.CreatedAt));
    }

    [Fact]
    public async Task GetPortfolio_ExistingPosition_ReturnsOnlyTheRequestedUsersPosition()
    {
        var (user, account) = await CreateUserAndGetAccountAsync();
        Guid stockId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
            stockId = (await dbContext.Stocks.SingleAsync(stock => stock.Symbol == "GOOG")).Id;
            dbContext.PortfolioPositions.Add(new PortfolioPosition(Guid.NewGuid(), account.Id, stockId, 7, 175m));
            await dbContext.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/users/{user.Id}/portfolio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var positions = await response.Content.ReadFromJsonAsync<List<PortfolioPositionResponse>>();
        var position = Assert.Single(positions!);
        Assert.Equal(account.Id, position.AccountId);
        Assert.Equal(stockId, position.StockId);
        Assert.Equal(7, position.Quantity);
        Assert.Equal(175m, position.AveragePrice);
    }

    private async Task<(UserResponse User, AccountResponse Account)> CreateUserAndGetAccountAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = $"order-user-{suffix}", email = $"order-user-{suffix}@example.test"
        });
        createResponse.EnsureSuccessStatusCode();
        var user = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        var accountResponse = await client.GetAsync($"/api/users/{user.Id}/account");
        accountResponse.EnsureSuccessStatusCode();
        var account = await accountResponse.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);
        return (user, account);
    }
}
