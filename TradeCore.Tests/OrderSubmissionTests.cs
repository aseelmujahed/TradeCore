using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCore.Api.Controllers;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.Messaging;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;
using TradeCore.Messaging;

namespace TradeCore.Tests;

public sealed class OrderSubmissionTests
{
    [Fact]
    public async Task CreateOrder_persists_pending_order_and_matching_api_outbox_message()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext, sellerShares: 4, buyPrice: 1m, sellQuantity: 4);
        var controller = CreateController(dbContext);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 4, 50m),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<OrderResponse>(created.Value);
        var outboxMessage = Assert.Single(await dbContext.OutboxMessages.ToListAsync());
        Assert.Equal(response.Id, outboxMessage.OrderId);
        Assert.Equal(OutboxMessage.ApiOwner, outboxMessage.Owner);
        Assert.Equal(OutboxMessage.OrderSubmittedMessageType, outboxMessage.MessageType);
        Assert.Null(outboxMessage.PublishedAt);
        var message = JsonSerializer.Deserialize<OrderSubmittedMessage>(outboxMessage.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(message);
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
    public async Task CreateOrder_does_not_depend_on_a_broker_publisher()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext);
        var controller = CreateController(dbContext);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(scenario.Buyer.Id, scenario.Stock.Symbol, OrderType.Buy, 2, 50m),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<OrderResponse>(created.Value);
        var persisted = await dbContext.Orders.SingleAsync(order => order.Id == response.Id);
        Assert.Equal(OrderStatus.Pending, persisted.Status);
        Assert.Equal(response.Id, (await dbContext.OutboxMessages.SingleAsync()).OrderId);
        Assert.Empty(await dbContext.Trades.ToListAsync());
    }

    [Fact]
    public async Task CreateOrder_with_unknown_account_does_not_persist_or_publish()
    {
        using var database = new TradingTestDatabase();
        await using var dbContext = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(dbContext);
        var controller = CreateController(dbContext);

        var result = await controller.CreateOrder(
            new CreateOrderRequest(Guid.NewGuid(), scenario.Stock.Symbol, OrderType.Buy, 2, 50m),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(2, await dbContext.Orders.CountAsync());
        Assert.Empty(await dbContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task CreateOrderWithOutbox_when_the_outbox_write_fails_rolls_back_the_order_write()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var baseOptions = new DbContextOptionsBuilder<TradeCore.Console.Data.TradeCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var setupContext = new TradeCore.Console.Data.TradeCoreDbContext(baseOptions))
        {
            await setupContext.Database.EnsureCreatedAsync();
            var stock = new TradeCore.Console.Models.Stock(Guid.NewGuid(), "OUTBOX", "Outbox Test Stock", 50m);
            var user = new TradeCore.Console.Models.User(Guid.NewGuid(), "outbox-user", "outbox@example.test");
            var account = new TradeCore.Console.Models.Account(Guid.NewGuid(), user.Id, "OUTBOX", 1_000m);
            setupContext.AddRange(stock, user, account);
            await setupContext.SaveChangesAsync();
        }

        var failingOptions = new DbContextOptionsBuilder<TradeCore.Console.Data.TradeCoreDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ThrowWhenOutboxIsAddedInterceptor())
            .Options;
        await using (var failingContext = new TradeCore.Console.Data.TradeCoreDbContext(failingOptions))
        {
            var accountId = await failingContext.Accounts.Select(account => account.Id).SingleAsync();
            var orderService = new OrderService(failingContext, new AccountService(failingContext), new StockService(failingContext));

            await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.CreateOrderWithOutboxAsync(
                accountId, "OUTBOX", OrderType.Buy, 2, 50m));
        }

        await using var verificationContext = new TradeCore.Console.Data.TradeCoreDbContext(baseOptions);
        Assert.Empty(await verificationContext.Orders.ToListAsync());
        Assert.Empty(await verificationContext.OutboxMessages.ToListAsync());
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
        TradeCore.Console.Data.TradeCoreDbContext dbContext) =>
        new(
            new OrderService(dbContext, new AccountService(dbContext), new StockService(dbContext)),
            NullLogger<OrdersController>.Instance);

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

    private sealed class ThrowWhenOutboxIsAddedInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<TradeCore.Console.Models.OutboxMessage>()
                .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new InvalidOperationException("Simulated outbox insert failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
