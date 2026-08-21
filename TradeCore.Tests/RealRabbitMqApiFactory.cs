using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TradeCore.Api.Messaging;
using TradeCore.Messaging;

namespace TradeCore.Tests;

/// <summary>API host configured to use the Testcontainers RabbitMQ instance and the production order publisher.</summary>
public sealed class RealRabbitMqApiFactory(RabbitMqTestSettings rabbitMq) : TradeCoreApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RabbitMq:Enabled", "true");
        builder.UseSetting("RabbitMq:HostName", rabbitMq.HostName);
        builder.UseSetting("RabbitMq:Port", rabbitMq.Port.ToString());
        builder.UseSetting("RabbitMq:UserName", rabbitMq.UserName);
        builder.UseSetting("RabbitMq:Password", rabbitMq.Password);
        builder.UseSetting("RabbitMq:OrdersQueue", rabbitMq.OrdersQueue);
        builder.UseSetting("RabbitMq:TradeExecutedQueue", rabbitMq.TradeExecutedQueue);
        builder.UseSetting("RabbitMq:StockPriceUpdatedQueue", rabbitMq.StockPriceUpdatedQueue);
        builder.UseSetting("RabbitMq:MaxProcessingAttempts", "3");
        builder.UseSetting("RabbitMq:RetryDelayMilliseconds", "100");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IOrderMessagePublisher>();
            services.AddScoped<IOrderMessagePublisher, RabbitMqOrderMessagePublisher>();
            services.AddHostedService<ApiOutboxPublisher>();
            services.AddHostedService<RabbitMqInitializationService>();
            services.AddHostedService<RabbitMqTradingEventConsumer>();
        });
    }

    public string GetDatabaseConnectionString() => DatabaseConnectionString;
}
