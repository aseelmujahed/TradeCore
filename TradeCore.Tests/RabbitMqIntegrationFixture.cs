using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Testcontainers.RabbitMq;
using TradeCore.Console.Data;
using TradeCore.Console.Services;
using TradeCore.Messaging;
using TradeCore.TradingEngine;

namespace TradeCore.Tests;

[CollectionDefinition(nameof(RealRabbitMqCollection), DisableParallelization = true)]
public sealed class RealRabbitMqCollection : ICollectionFixture<RabbitMqIntegrationFixture>
{
}

public sealed record RabbitMqTestSettings(
    string HostName,
    int Port,
    string UserName,
    string Password,
    string OrdersQueue,
    string TradeExecutedQueue,
    string StockPriceUpdatedQueue);

/// <summary>Starts one isolated broker and production API/worker RabbitMQ transports for real-broker tests.</summary>
public sealed class RabbitMqIntegrationFixture : IAsyncLifetime
{
    private const string UserName = "tradecore";
    private const string Password = "tradecore-tests";
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();
    private IHost? _tradingEngine;

    public RealRabbitMqApiFactory ApiFactory { get; private set; } = null!;
    public RabbitMqTestSettings Settings { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _rabbitMq.StartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        Settings = new RabbitMqTestSettings(
            _rabbitMq.Hostname,
            _rabbitMq.GetMappedPublicPort(5672),
            UserName,
            Password,
            $"orders-{suffix}",
            $"trade-executed-{suffix}",
            $"stock-price-updated-{suffix}");

        ApiFactory = new RealRabbitMqApiFactory(Settings);
        _ = ApiFactory.Server; // Starts API hosted services, including RabbitMqTradingEventConsumer.

        _tradingEngine = CreateTradingEngine(ApiFactory.GetDatabaseConnectionString(), Settings);
        await _tradingEngine.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_tradingEngine is not null)
        {
            await _tradingEngine.StopAsync();
            _tradingEngine.Dispose();
        }

        ApiFactory?.Dispose();
        await _rabbitMq.DisposeAsync();
    }

    private static IHost CreateTradingEngine(string connectionString, RabbitMqTestSettings settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Enabled"] = "true",
            ["RabbitMq:HostName"] = settings.HostName,
            ["RabbitMq:Port"] = settings.Port.ToString(),
            ["RabbitMq:UserName"] = settings.UserName,
            ["RabbitMq:Password"] = settings.Password,
            ["RabbitMq:OrdersQueue"] = settings.OrdersQueue,
            ["RabbitMq:TradeExecutedQueue"] = settings.TradeExecutedQueue,
            ["RabbitMq:StockPriceUpdatedQueue"] = settings.StockPriceUpdatedQueue,
            ["RabbitMq:MaxProcessingAttempts"] = "3",
            ["RabbitMq:RetryDelayMilliseconds"] = "100"
        });

        builder.Services.AddDbContext<TradeCoreDbContext>(options => options.UseSqlite(connectionString));
        builder.Services.AddOptions<RabbitMqOptions>()
            .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<StockService>();
        builder.Services.AddScoped<OrderBookService>();
        builder.Services.AddScoped<OrderMatchingService>();
        builder.Services.AddScoped<PortfolioService>();
        builder.Services.AddScoped<TradeCreationService>();
        builder.Services.AddSingleton<StockProcessingLockRegistry>();
        builder.Services.AddScoped<OrderProcessingService>();
        builder.Services.AddSingleton<ITradingEventPublisher, RabbitMqTradingEventPublisher>();
        builder.Services.AddSingleton<IOrderMessageHandler, OrderMessageHandler>();
        builder.Services.AddSingleton<RabbitMqOrderDeliveryTransport>();
        builder.Services.AddSingleton<IOrderMessageDeliveryTransport>(provider =>
            provider.GetRequiredService<RabbitMqOrderDeliveryTransport>());
        builder.Services.AddSingleton<ReliableOrderDeliveryProcessor>();
        builder.Services.AddHostedService<RabbitMqOrderConsumer>();

        return builder.Build();
    }
}
