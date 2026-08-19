using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCore.Api.Controllers;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.Notifications;
using TradeCore.Console.Enums;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class OrdersApiRequestBindingTests
{
    [Theory]
    [InlineData("Buy", OrderType.Buy)]
    [InlineData("Sell", OrderType.Sell)]
    public async Task CreateOrder_JsonOrderType_BindsReturnsAndPersistsTheRequestedType(string requestedType, OrderType expectedType)
    {
        using var database = new TradingTestDatabase();
        Guid orderId;
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        serializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));

        await using (var context = database.CreateContext())
        {
            var scenario = await database.SeedScenarioAsync(context, sellerShares: 10);
            var request = JsonSerializer.Deserialize<CreateOrderRequest>(
                $$"""{"accountId":"{{scenario.Seller.Id}}","stockSymbol":"{{scenario.Stock.Symbol}}","orderType":"{{requestedType}}","quantity":2,"price":450}""",
                serializerOptions);
            Assert.NotNull(request);

            var result = await CreateController(context).CreateOrder(request, CancellationToken.None);
            var created = Assert.IsType<CreatedAtActionResult>(result.Result);
            var response = Assert.IsType<OrderResponse>(created.Value);
            Assert.Equal(expectedType, response.Type);
            orderId = response.Id;
        }

        await using var freshContext = database.CreateContext();
        var persisted = await freshContext.Orders.SingleAsync(order => order.Id == orderId);
        Assert.Equal(expectedType, persisted.Type);
    }

    [Fact]
    public void CreateOrder_InvalidJsonOrderType_IsRejectedInsteadOfDefaultingToBuy()
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        serializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateOrderRequest>(
            """{"accountId":"223a4a06-42a2-416d-94f1-cab6475d3015","stockSymbol":"MSFT","orderType":"Hold","quantity":2,"price":450}""",
            serializerOptions));
    }

    private static OrdersController CreateController(TradeCore.Console.Data.TradeCoreDbContext context)
    {
        var accountService = new AccountService(context);
        var stockService = new StockService(context);
        var portfolioService = new PortfolioService(context, accountService, stockService);
        var orderBookService = new OrderBookService(context);
        var matchingService = new OrderMatchingService(orderBookService);
        var tradeCreationService = new TradeCreationService(context, matchingService, portfolioService);
        return new OrdersController(
            new OrderService(context, accountService, stockService),
            new OrderProcessingService(context, tradeCreationService),
            new NoOpTradeExecutionNotifier(),
            new NoOpStockPriceNotifier(),
            NullLogger<OrdersController>.Instance);
    }

    private sealed class NoOpTradeExecutionNotifier : ITradeExecutionNotifier
    {
        public Task NotifyTradeExecutedAsync(TradeCore.Api.DTOs.Trades.TradeResponse trade, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpStockPriceNotifier : IStockPriceNotifier
    {
        public Task NotifyStockPriceUpdatedAsync(TradeCore.Api.DTOs.Stocks.StockPriceUpdatedResponse update, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
