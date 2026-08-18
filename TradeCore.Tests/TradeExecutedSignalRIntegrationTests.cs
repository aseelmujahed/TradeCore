using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeCore.Api.DTOs.Trades;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Tests;

public sealed class TradeExecutedSignalRIntegrationTests : IClassFixture<TradeCoreApiFactory>
{
    private readonly TradeCoreApiFactory _factory;

    public TradeExecutedSignalRIntegrationTests(TradeCoreApiFactory factory) => _factory = factory;

    [Fact]
    public async Task MatchingOrder_BroadcastsPersistedExactFillTrade()
    {
        var scenario = await SeedScenarioAsync(sellQuantities: [4]);
        var received = new TaskCompletionSource<TradeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await ConnectAsync(trade => received.TrySetResult(trade));

        var response = await SubmitBuyOrderAsync(scenario, 4, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var persisted = await GetPersistedTradesAsync(scenario.StockId);
        var trade = Assert.Single(persisted);
        Assert.Equal(trade.Id, notification.Id);
        Assert.Equal(trade.BuyOrderId, notification.BuyOrderId);
        Assert.Equal(trade.SellOrderId, notification.SellOrderId);
        Assert.Equal(trade.StockId, notification.StockId);
        Assert.Equal(trade.Quantity, notification.Quantity);
        Assert.Equal(trade.Price, notification.Price);
        Assert.Equal(trade.ExecutedAt, notification.ExecutedAt);
    }

    [Fact]
    public async Task PartialFill_BroadcastsOneEventForTheCreatedTrade()
    {
        var scenario = await SeedScenarioAsync(sellQuantities: [4]);
        var received = new TaskCompletionSource<TradeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await ConnectAsync(trade => received.TrySetResult(trade));

        var response = await SubmitBuyOrderAsync(scenario, 10, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(4, notification.Quantity);
        await Task.Delay(200);
        Assert.Single(await GetPersistedTradesAsync(scenario.StockId));
    }

    [Fact]
    public async Task IncomingOrderMatchingMultipleOppositeOrders_BroadcastsEveryDistinctTradeOnce()
    {
        var scenario = await SeedScenarioAsync(sellQuantities: [2, 3]);
        var received = new ConcurrentQueue<TradeResponse>();
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await ConnectAsync(trade =>
        {
            received.Enqueue(trade);
            if (received.Count == 2)
            {
                allReceived.TrySetResult();
            }
        });

        var response = await SubmitBuyOrderAsync(scenario, 5, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var persisted = await GetPersistedTradesAsync(scenario.StockId);
        Assert.Equal(2, persisted.Count);
        Assert.Equal(2, received.Count);
        Assert.Equal(2, received.Select(trade => trade.Id).Distinct().Count());
        Assert.Equal(
            persisted.Select(trade => trade.Id).OrderBy(id => id),
            received.Select(trade => trade.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task UnmatchedOrder_DoesNotBroadcastTradeExecuted()
    {
        var scenario = await SeedScenarioAsync(sellQuantities: [4], sellPrice: 60m);
        var received = new TaskCompletionSource<TradeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await ConnectAsync(trade => received.TrySetResult(trade));

        var response = await SubmitBuyOrderAsync(scenario, 4, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await Task.Delay(300);
        Assert.False(received.Task.IsCompleted);
        Assert.Empty(await GetPersistedTradesAsync(scenario.StockId));
    }

    [Fact]
    public async Task FailedMultiTradeProcessing_RollsBackAndDoesNotBroadcastTradeExecuted()
    {
        var scenario = await SeedFailedMultiTradeScenarioAsync();
        var received = new TaskCompletionSource<TradeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await ConnectAsync(trade => received.TrySetResult(trade));

        var response = await SubmitSellOrderAsync(scenario, 2, 50m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await Task.Delay(300);
        Assert.False(received.Task.IsCompleted);
        Assert.Empty(await GetPersistedTradesAsync(scenario.StockId));
    }

    [Fact]
    public async Task ConcurrentOrderProcessing_BroadcastsEachPersistedTradeOnlyOnce()
    {
        var scenario = await SeedScenarioAsync(sellQuantities: [6]);
        var received = new ConcurrentQueue<TradeResponse>();
        await using var connection = await ConnectAsync(trade => received.Enqueue(trade));

        var responses = await Task.WhenAll(
            SubmitBuyOrderAsync(scenario, 3, 50m),
            SubmitBuyOrderAsync(scenario, 3, 50m));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var persisted = await WaitForPersistedTradesAsync(scenario.StockId, expectedCount: 2);
        await Task.Delay(300);
        Assert.Equal(persisted.Count, received.Count);
        Assert.Equal(persisted.Count, received.Select(trade => trade.Id).Distinct().Count());
        Assert.Equal(
            persisted.Select(trade => trade.Id).OrderBy(id => id),
            received.Select(trade => trade.Id).OrderBy(id => id));
    }

    private async Task<HubConnection> ConnectAsync(Action<TradeResponse> onTradeExecuted)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/trading"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        connection.On("TradeExecuted", onTradeExecuted);
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

    private async Task<HttpResponseMessage> SubmitSellOrderAsync(FailedTradingScenario scenario, int quantity, decimal price)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/orders", new
        {
            accountId = scenario.SellerAccountId,
            stockSymbol = scenario.StockSymbol,
            orderType = "Sell",
            quantity,
            price
        });
    }

    private async Task<TradingScenario> SeedScenarioAsync(int[] sellQuantities, decimal sellPrice = 50m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var stock = new Stock(Guid.NewGuid(), $"T{suffix}", "SignalR Test Stock", 50m);
        var buyerUser = new User(Guid.NewGuid(), $"buyer-{suffix}", $"buyer-{suffix}@example.test");
        var sellerUser = new User(Guid.NewGuid(), $"seller-{suffix}", $"seller-{suffix}@example.test");
        var buyer = new Account(Guid.NewGuid(), buyerUser.Id, $"B{suffix}", 10_000m);
        var seller = new Account(Guid.NewGuid(), sellerUser.Id, $"S{suffix}", 100m);
        dbContext.AddRange(stock, buyerUser, sellerUser, buyer, seller,
            new PortfolioPosition(Guid.NewGuid(), seller.Id, stock.Id, sellQuantities.Sum(), 40m));
        foreach (var quantity in sellQuantities)
        {
            dbContext.Orders.Add(new Order(Guid.NewGuid(), seller.Id, stock.Id, OrderType.Sell, quantity, sellPrice));
        }

        await dbContext.SaveChangesAsync();
        return new TradingScenario(stock.Id, stock.Symbol, buyer.Id);
    }

    private async Task<FailedTradingScenario> SeedFailedMultiTradeScenarioAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var stock = new Stock(Guid.NewGuid(), $"F{suffix}", "Failed SignalR Test Stock", 50m);
        var fundedUser = new User(Guid.NewGuid(), $"funded-{suffix}", $"funded-{suffix}@example.test");
        var unfundedUser = new User(Guid.NewGuid(), $"unfunded-{suffix}", $"unfunded-{suffix}@example.test");
        var sellerUser = new User(Guid.NewGuid(), $"seller-{suffix}", $"seller-{suffix}@example.test");
        var fundedBuyer = new Account(Guid.NewGuid(), fundedUser.Id, $"F{suffix}", 1_000m);
        var unfundedBuyer = new Account(Guid.NewGuid(), unfundedUser.Id, $"U{suffix}", 10m);
        var seller = new Account(Guid.NewGuid(), sellerUser.Id, $"S{suffix}", 100m);
        dbContext.AddRange(stock, fundedUser, unfundedUser, sellerUser, fundedBuyer, unfundedBuyer, seller,
            new Order(Guid.NewGuid(), fundedBuyer.Id, stock.Id, OrderType.Buy, 1, 51m),
            new Order(Guid.NewGuid(), unfundedBuyer.Id, stock.Id, OrderType.Buy, 1, 50m),
            new PortfolioPosition(Guid.NewGuid(), seller.Id, stock.Id, 2, 40m));
        await dbContext.SaveChangesAsync();
        return new FailedTradingScenario(stock.Id, stock.Symbol, seller.Id);
    }

    private async Task<IReadOnlyList<Trade>> GetPersistedTradesAsync(Guid stockId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        return await dbContext.Trades.Where(trade => trade.StockId == stockId).ToListAsync();
    }

    private async Task<IReadOnlyList<Trade>> WaitForPersistedTradesAsync(Guid stockId, int expectedCount)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var trades = await GetPersistedTradesAsync(stockId);
            if (trades.Count == expectedCount)
            {
                return trades;
            }

            await Task.Delay(100);
        }

        return await GetPersistedTradesAsync(stockId);
    }

    private sealed record TradingScenario(Guid StockId, string StockSymbol, Guid BuyerAccountId);

    private sealed record FailedTradingScenario(Guid StockId, string StockSymbol, Guid SellerAccountId);
}
