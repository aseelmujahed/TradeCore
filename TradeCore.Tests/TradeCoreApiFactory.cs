using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeCore.Api.Messaging;
using TradeCore.Console.Data;
using TradeCore.Messaging;
using TradeCore.TradingEngine;

namespace TradeCore.Tests;

public class TradeCoreApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tradecore-tests-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;

    protected string DatabaseConnectionString => _connectionString;

    public TradeCoreApiFactory()
    {
        _connectionString = $"Data Source={_databasePath};Cache=Shared;Default Timeout=30";

        var options = new DbContextOptionsBuilder<TradeCoreDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        using var dbContext = new TradeCoreDbContext(options);
        dbContext.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TradeCoreDatabase", "Host=localhost;Database=tradecore_test");
        builder.UseSetting("RabbitMq:Enabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TradeCoreDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TradeCoreDbContext>>();
            services.RemoveAll<TradeCoreDbContext>();
            services.AddDbContext<TradeCoreDbContext>(options => options.UseSqlite(_connectionString));
            services.RemoveAll<IOrderMessagePublisher>();
            services.AddSingleton<OrderProcessingMonitor>();
            services.AddSingleton<TestTradingEventPublisher>();
            services.AddScoped<IOrderMessagePublisher, InProcessTradingEngineOrderPublisher>();
        });
    }

    public Task<Exception?> WaitForOrderProcessingAsync(Guid orderId, TimeSpan timeout) =>
        Services.GetRequiredService<OrderProcessingMonitor>().WaitAsync(orderId, timeout);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // A long-polling SignalR request can release its SQLite connection after fixture teardown.
            }
        }
    }

    /// <summary>Exercises the worker's real message handler while replacing only the external broker in API tests.</summary>
    private sealed class InProcessTradingEngineOrderPublisher(
        IServiceScopeFactory scopeFactory,
        TestTradingEventPublisher tradingEventPublisher,
        OrderProcessingMonitor monitor) : IOrderMessagePublisher
    {
        public Task PublishAsync(OrderSubmittedMessage message, CancellationToken cancellationToken)
        {
            var completion = monitor.Begin(message.OrderId);
            _ = ProcessAsync(message, completion, cancellationToken);
            return Task.CompletedTask;
        }

        private async Task ProcessAsync(OrderSubmittedMessage message, TaskCompletionSource<Exception?> completion, CancellationToken cancellationToken)
        {
            try
            {
                var handler = new OrderMessageHandler(scopeFactory, tradingEventPublisher);
                var body = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await handler.ProcessAsync(body, cancellationToken);
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetResult(exception);
            }
        }
    }

    private sealed class TestTradingEventPublisher(IServiceScopeFactory scopeFactory) : ITradingEventPublisher
    {
        public Task PublishAsync(TradeExecutedEvent message, CancellationToken cancellationToken) =>
            DispatchAsync(handler => handler.HandleAsync(message, cancellationToken));

        public Task PublishAsync(StockPriceUpdatedEvent message, CancellationToken cancellationToken) =>
            DispatchAsync(handler => handler.HandleAsync(message, cancellationToken));

        private async Task DispatchAsync(Func<TradingEventNotificationHandler, Task> dispatch)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await dispatch(scope.ServiceProvider.GetRequiredService<TradingEventNotificationHandler>());
        }
    }

    private sealed class OrderProcessingMonitor
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Exception?>> _completions = new();

        public TaskCompletionSource<Exception?> Begin(Guid orderId)
        {
            var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_completions.TryAdd(orderId, completion))
            {
                throw new InvalidOperationException($"Order {orderId} was published more than once in this test host.");
            }
            return completion;
        }

        public Task<Exception?> WaitAsync(Guid orderId, TimeSpan timeout)
        {
            if (!_completions.TryGetValue(orderId, out var completion))
            {
                throw new InvalidOperationException($"Order {orderId} has not been published.");
            }
            return completion.Task.WaitAsync(timeout);
        }
    }
}
