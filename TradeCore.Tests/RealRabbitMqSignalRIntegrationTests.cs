using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeCore.Api.DTOs.Stocks;
using TradeCore.Api.DTOs.Trades;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Tests;

[Collection(nameof(RealRabbitMqCollection))]
public sealed class RealRabbitMqSignalRIntegrationTests(RabbitMqIntegrationFixture fixture)
{
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TradeExecuted_EndToEnd_ThroughRealRabbitMq_ReachesSignalRClient()
    {
        var scenario = await SeedScenarioAsync(sellPrice: 45m);
        var notification = new TaskCompletionSource<TradeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedCount = 0;
        await using var connection = await ConnectAsync(
            onTradeExecuted: trade =>
            {
                if (trade.StockId != scenario.StockId) return;
                if (Interlocked.Increment(ref receivedCount) == 1) notification.TrySetResult(trade);
                else duplicate.TrySetResult();
            });

        var response = await SubmitBuyOrderAsync(scenario, 4, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var received = await notification.Task.WaitAsync(NotificationTimeout);
        var persisted = Assert.Single(await GetTradesAsync(scenario.StockId));
        Assert.Equal(persisted.Id, received.Id);
        Assert.Equal(persisted.BuyOrderId, received.BuyOrderId);
        Assert.Equal(persisted.SellOrderId, received.SellOrderId);
        Assert.Equal(scenario.StockId, received.StockId);
        Assert.Equal(4, received.Quantity);
        Assert.Equal(45m, received.Price);
        Assert.Equal(persisted.ExecutedAt, received.ExecutedAt);
        await AssertNoDuplicateAsync(duplicate);
        Assert.Equal(1, Volatile.Read(ref receivedCount));
    }

    [Fact]
    public async Task StockPriceUpdated_EndToEnd_ThroughRealRabbitMq_ReachesSignalRClient()
    {
        var scenario = await SeedScenarioAsync(sellPrice: 45m);
        var notification = new TaskCompletionSource<StockPriceUpdatedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedCount = 0;
        await using var connection = await ConnectAsync(
            onStockPriceUpdated: update =>
            {
                if (update.StockId != scenario.StockId) return;
                if (Interlocked.Increment(ref receivedCount) == 1) notification.TrySetResult(update);
                else duplicate.TrySetResult();
            });

        var response = await SubmitBuyOrderAsync(scenario, 4, 50m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var received = await notification.Task.WaitAsync(NotificationTimeout);
        var stock = await GetStockAsync(scenario.StockId);
        Assert.Equal(scenario.StockId, received.StockId);
        Assert.Equal(scenario.StockSymbol, received.Symbol);
        Assert.Equal(45m, received.Price);
        Assert.Equal(stock.CurrentPrice, received.Price);
        await AssertNoDuplicateAsync(duplicate);
        Assert.Equal(1, Volatile.Read(ref receivedCount));
    }

    private async Task<HubConnection> ConnectAsync(
        Action<TradeResponse>? onTradeExecuted = null,
        Action<StockPriceUpdatedResponse>? onStockPriceUpdated = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.ApiFactory.Server.BaseAddress, "/hubs/trading"), options =>
            {
                options.HttpMessageHandlerFactory = _ => fixture.ApiFactory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        if (onTradeExecuted is not null) connection.On("TradeExecuted", onTradeExecuted);
        if (onStockPriceUpdated is not null) connection.On("StockPriceUpdated", onStockPriceUpdated);
        await connection.StartAsync();
        return connection;
    }

    private async Task<HttpResponseMessage> SubmitBuyOrderAsync(TradingScenario scenario, int quantity, decimal price)
    {
        using var client = fixture.ApiFactory.CreateClient();
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
        await using var scope = fixture.ApiFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        var stock = new Stock(Guid.NewGuid(), $"R{suffix}", "Real RabbitMQ SignalR Test Stock", 20m);
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

    private async Task<IReadOnlyList<Trade>> GetTradesAsync(Guid stockId)
    {
        await using var scope = fixture.ApiFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        return await dbContext.Trades.Where(trade => trade.StockId == stockId).ToListAsync();
    }

    private async Task<Stock> GetStockAsync(Guid stockId)
    {
        await using var scope = fixture.ApiFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
        return await dbContext.Stocks.SingleAsync(stock => stock.Id == stockId);
    }

    private static async Task AssertNoDuplicateAsync(TaskCompletionSource duplicate) =>
        await Assert.ThrowsAsync<TimeoutException>(() => duplicate.Task.WaitAsync(TimeSpan.FromMilliseconds(500)));

    private sealed record TradingScenario(Guid StockId, string StockSymbol, Guid BuyerAccountId);
}
