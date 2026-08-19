using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeCore.Api.Controllers;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.Messaging;
using TradeCore.Console.Enums;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class OrderSubmissionTests
{
    [Fact]
    public async Task CreateOrder_persists_pending_order_publishes_once_and_does_not_process_a_compatible_match()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 4, buyPrice: 1m, sellQuantity: 4);
        var publisher = new RecordingOrderMessagePublisher();
        var controller = CreateController(dbContext, publisher);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 4, 50m),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<OrderResponse>(created.Value);
        var message = Assert.Single(publisher.Messages);
        Assert.Equal(response.Id, message.OrderId);
        Assert.Equal(OrderStatus.Pending, response.Status);
        Assert.Equal(OrderStatus.Pending, (await dbContext.Orders.SingleAsync(order => order.Id == response.Id)).Status);
        Assert.Empty(await dbContext.Trades.ToListAsync());
        Assert.Equal(1_000m, (await dbContext.Accounts.SingleAsync(account => account.Id == scenario.Buyer.Id)).Balance);
        Assert.Equal(100m, (await dbContext.Accounts.SingleAsync(account => account.Id == scenario.Seller.Id)).Balance);
        Assert.Empty(await dbContext.PortfolioPositions.Where(position => position.AccountId == scenario.Buyer.Id).ToListAsync());
        var sellerPosition = await dbContext.PortfolioPositions.SingleAsync(position => position.AccountId == scenario.Seller.Id);
        Assert.Equal(4, sellerPosition.Quantity);
    }

    [Fact]
    public async Task CreateOrder_when_publishing_fails_keeps_the_pending_order_and_does_not_report_success()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext);
        var publisher = new RecordingOrderMessagePublisher { Exception = new InvalidOperationException("Broker unavailable") };
        var controller = CreateController(dbContext, publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.CreateOrder(
            new CreateOrderRequest(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 2, 50m),
            CancellationToken.None));

        Assert.Single(publisher.Messages);
        var persisted = await dbContext.Orders.SingleAsync(order => order.Id == publisher.Messages[0].OrderId);
        Assert.Equal(OrderStatus.Pending, persisted.Status);
        Assert.Empty(await dbContext.Trades.ToListAsync());
    }

    [Fact]
    public async Task CreateOrder_with_unknown_account_does_not_persist_or_publish()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext);
        var publisher = new RecordingOrderMessagePublisher();
        var controller = CreateController(dbContext, publisher);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(Guid.NewGuid(), scenario.Stock.Symbol, OrderType.Buy, 2, 50m),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(publisher.Messages);
        Assert.Equal(2, await dbContext.Orders.CountAsync());
    }

    [Fact]
    public async Task RabbitMqPublisher_serializes_only_the_order_id_as_a_persistent_json_message()
    {
        var transport = new RecordingRabbitMqConnectionService();
        var publisher = new RabbitMqOrderMessagePublisher(
            transport,
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions { OrdersQueue = "configured-orders" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RabbitMqOrderMessagePublisher>.Instance);
        var orderId = Guid.NewGuid();

        await publisher.PublishAsync(new OrderSubmittedMessage(orderId), CancellationToken.None);

        var message = Assert.Single(transport.Messages);
        Assert.Equal("configured-orders", message.RoutingKey);
        Assert.True(message.Persistent);
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal(orderId.ToString(), message.MessageId);
        using var document = JsonDocument.Parse(message.Body);
        Assert.Equal(orderId, document.RootElement.GetProperty("orderId").GetGuid());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    private static OrdersController CreateController(
        TradeCore.Console.Data.TradeCoreDbContext dbContext,
        IOrderMessagePublisher publisher) =>
        new(
            new OrderService(dbContext, new AccountService(dbContext), new StockService(dbContext)),
            publisher);

    private sealed class RecordingOrderMessagePublisher : IOrderMessagePublisher
    {
        public List<OrderSubmittedMessage> Messages { get; } = [];
        public Exception? Exception { get; init; }

        public Task PublishAsync(OrderSubmittedMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }

    private sealed class RecordingRabbitMqConnectionService : IRabbitMqConnectionService
    {
        public List<RabbitMqPublishedMessage> Messages { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(RabbitMqPublishedMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
