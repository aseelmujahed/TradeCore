using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;
using TradeCore.Messaging;

namespace TradeCore.Tests;

public sealed class StockPriceUpdateTests
{
    [Fact]
    public async Task ProcessOrderAsync_ExactFill_PersistsExecutionPriceAsCurrentStockPrice()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellPrice: 45m, buyPrice: 1m, sellQuantity: 4);
        var submitted = await CreateBuyOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);

        var result = await database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submitted);

        Assert.Equal(45m, (await dbContext.Stocks.SingleAsync(stock => stock.Id == scenario.Stock.Id)).CurrentPrice);
        Assert.NotNull(result.StockPriceUpdate);
        Assert.Equal(scenario.Stock.Id, result.StockPriceUpdate.StockId);
        Assert.Equal(scenario.Stock.Symbol, result.StockPriceUpdate.Symbol);
        Assert.Equal(45m, result.StockPriceUpdate.Price);
        var outboxMessages = await dbContext.OutboxMessages
            .Where(message => message.Owner == OutboxMessage.TradingEngineOwner)
            .ToListAsync();
        var tradeEvent = JsonSerializer.Deserialize<TradeExecutedEvent>(Assert.Single(outboxMessages,
            message => message.MessageType == OutboxMessage.TradeExecutedMessageType).Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var stockPriceEvent = JsonSerializer.Deserialize<StockPriceUpdatedEvent>(Assert.Single(outboxMessages,
            message => message.MessageType == OutboxMessage.StockPriceUpdatedMessageType).Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(tradeEvent);
        Assert.NotNull(stockPriceEvent);
        Assert.Equal(tradeEvent.TradeId, tradeEvent.EventId);
        Assert.Equal(tradeEvent.TradeId, stockPriceEvent.EventId);
        Assert.Equal(result.StockPriceUpdate.Price, stockPriceEvent.Price);
    }

    [Fact]
    public async Task ProcessOrderAsync_NoMatch_LeavesCurrentStockPriceUnchanged()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellPrice: 60m, buyPrice: 1m, sellQuantity: 4);
        var submitted = await CreateBuyOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);

        var result = await database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submitted);

        Assert.False(result.HasTrade);
        Assert.Null(result.StockPriceUpdate);
        Assert.Equal(50m, (await dbContext.Stocks.SingleAsync(stock => stock.Id == scenario.Stock.Id)).CurrentPrice);
    }

    [Fact]
    public async Task ProcessOrderAsync_PartialFill_UsesActualExecutionPrice()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellPrice: 45m, buyPrice: 1m, sellQuantity: 4, sellerShares: 4);
        var submitted = await CreateBuyOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 10, 50m);

        await database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submitted);

        Assert.Equal(45m, (await dbContext.Stocks.SingleAsync(stock => stock.Id == scenario.Stock.Id)).CurrentPrice);
        Assert.Equal(4, Assert.Single(await dbContext.Trades.ToListAsync()).Quantity);
    }

    [Fact]
    public async Task ProcessOrderAsync_MultipleMatches_UsesLastDeterministicExecutionPrice()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellPrice: 440m, buyPrice: 1m, sellQuantity: 1, sellerShares: 2);
        var secondSell = new Order(Guid.NewGuid(), scenario.Seller.Id, scenario.Stock.Id, OrderType.Sell, 1, 450m);
        dbContext.Orders.Add(secondSell);
        await dbContext.SaveChangesAsync();
        var submitted = await CreateBuyOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 2, 500m);

        var result = await database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submitted);

        Assert.Equal(2, result.Trades.Count);
        Assert.Equal([440m, 450m], result.Trades.Select(trade => trade.Price));
        Assert.Equal(450m, (await dbContext.Stocks.SingleAsync(stock => stock.Id == scenario.Stock.Id)).CurrentPrice);
        Assert.Equal(450m, result.StockPriceUpdate!.Price);
        var outboxMessages = await dbContext.OutboxMessages
            .Where(message => message.Owner == OutboxMessage.TradingEngineOwner)
            .ToListAsync();
        Assert.Equal(2, outboxMessages.Count(message => message.MessageType == OutboxMessage.TradeExecutedMessageType));
        var stockPriceMessage = Assert.Single(outboxMessages, message => message.MessageType == OutboxMessage.StockPriceUpdatedMessageType);
        var stockPriceEvent = JsonSerializer.Deserialize<StockPriceUpdatedEvent>(stockPriceMessage.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(stockPriceEvent);
        Assert.Equal(result.Trades[^1].Id, stockPriceEvent.EventId);
    }

    [Fact]
    public async Task ProcessOrderAsync_FailedTrade_LeavesCurrentStockPriceUnchanged()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, buyerBalance: 100m, sellPrice: 45m, buyPrice: 1m, sellQuantity: 4, sellerShares: 4);
        var submitted = await CreateBuyOrderAsync(dbContext, scenario.Buyer.Id, scenario.Stock, 4, 50m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.CreateServices(dbContext).OrderProcessingService.ProcessOrderAsync(submitted));

        Assert.Empty(await dbContext.Trades.ToListAsync());
        Assert.Empty(await dbContext.OutboxMessages.Where(message => message.Owner == OutboxMessage.TradingEngineOwner).ToListAsync());
        Assert.Equal(50m, (await dbContext.Stocks.SingleAsync(stock => stock.Id == scenario.Stock.Id)).CurrentPrice);
    }

    private static async Task<Order> CreateBuyOrderAsync(
        TradeCore.Console.Data.TradeCoreDbContext dbContext,
        Guid accountId,
        Stock stock,
        int quantity,
        decimal price)
    {
        return await new OrderService(
            dbContext,
            new AccountService(dbContext),
            new StockService(dbContext)).CreateOrderAsync(accountId, stock.Symbol, OrderType.Buy, quantity, price);
    }
}
