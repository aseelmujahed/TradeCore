using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.DTOs.Stocks;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Tests;

public sealed class StockPriceSignalRIntegrationTests : IClassFixture<TradeCoreApiFactory>
{
    private readonly TradeCoreApiFactory _factory;

    public StockPriceSignalRIntegrationTests(TradeCoreApiFactory factory) => _factory = factory;

    [Fact]
    public async Task MatchingOrder_UpdatesPersistedStockPriceAndBroadcastsExecutionPrice()
    {
        var scenario = await SeedScenarioAsync(sellPrice: 45m);
        var received = new TaskCompletionSource<StockPriceUpdatedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedCount = 0;
        await using var connection = await ConnectAsync(update =>
        {
            if (update.StockId == scenario.StockId)
            {
                Interlocked.Increment(ref receivedCount);
                received.TrySetResult(update);
            }
        });

        var response = await SubmitBuyOrderAsync(scenario, 4, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(await WaitForProcessingAsync(response));
        var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stock = await GetStockAsync(scenario.StockId);
        Assert.Equal(scenario.StockId, update.StockId);
        Assert.Equal(scenario.StockSymbol, update.Symbol);
        Assert.Equal(45m, update.Price);
        Assert.Equal(45m, stock.CurrentPrice);
        Assert.Equal(1, Volatile.Read(ref receivedCount));
    }

    [Fact]
    public async Task UnmatchedOrder_DoesNotChangeStockPriceOrBroadcastUpdate()
    {
        var scenario = await SeedScenarioAsync(sellPrice: 60m);
        var received = new TaskCompletionSource<StockPriceUpdatedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await ConnectAsync(update =>
        {
            if (update.StockId == scenario.StockId) received.TrySetResult(update);
        });

        var response = await SubmitBuyOrderAsync(scenario, 4, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(await WaitForProcessingAsync(response));
        Assert.False(received.Task.IsCompleted);
        Assert.Equal(20m, (await GetStockAsync(scenario.StockId)).CurrentPrice);
    }

    private async Task<HubConnection> ConnectAsync(Action<StockPriceUpdatedResponse> onStockPriceUpdated)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/trading"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        connection.On("StockPriceUpdated", onStockPriceUpdated);
        await connection.StartAsync();
        return connection;
    }

    private async Task<HttpResponseMessage> SubmitBuyOrderAsync(TradingScenario scenario, int quantity, decimal price)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/orders", new
        {
            accountId = scenario.BuyerAccountId,
            stockSymbol = scenario.StockSymbol,
            orderType = "Buy",
            quantity,
            price
        });
    }

    private async Task<TradingScenario> SeedScenarioAsync(decimal sellPrice)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var stock = new Stock(Guid.NewGuid(), $"P{suffix}", "Stock Price SignalR Test", 20m);
        var buyerUser = new User(Guid.NewGuid(), $"buyer-{suffix}", $"buyer-{suffix}@example.test");
        var sellerUser = new User(Guid.NewGuid(), $"seller-{suffix}", $"seller-{suffix}@example.test");
        var buyer = new Account(Guid.NewGuid(), buyerUser.Id, $"B{suffix}", 10_000m);
        var seller = new Account(Guid.NewGuid(), sellerUser.Id, $"S{suffix}", 100m);
        dbContext.AddRange(stock, buyerUser, sellerUser, buyer, seller,
            new Order(Guid.NewGuid(), seller.Id, stock.Id, OrderType.Sell, 4, sellPrice),
            new PortfolioPosition(Guid.NewGuid(), seller.Id, stock.Id, 4, 20m));
        await dbContext.SaveChangesAsync();
        return new TradingScenario(stock.Id, stock.Symbol, buyer.Id);
    }

    private async Task<Stock> GetStockAsync(Guid stockId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        return await dbContext.Stocks.SingleAsync(stock => stock.Id == stockId);
    }

    private async Task<Exception?> WaitForProcessingAsync(HttpResponseMessage response)
    {
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        return await _factory.WaitForOrderProcessingAsync(order.Id, TimeSpan.FromSeconds(3));
    }

    private sealed record TradingScenario(Guid StockId, string StockSymbol, Guid BuyerAccountId);
}
