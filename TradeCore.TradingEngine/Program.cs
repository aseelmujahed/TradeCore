using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradeCore.Console.Data;
using TradeCore.Console.Services;
using TradeCore.Messaging;
namespace TradeCore.TradingEngine;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var connectionString = builder.Configuration.GetConnectionString("TradeCoreDatabase")
            ?? throw new InvalidOperationException(
                "The required 'ConnectionStrings:TradeCoreDatabase' configuration setting is missing.");

        builder.Services.AddDbContext<TradeCoreDbContext>(options => options.UseNpgsql(connectionString));
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
        builder.Services.AddSingleton<IOrderMessageDeliveryTransport>(serviceProvider =>
            serviceProvider.GetRequiredService<RabbitMqOrderDeliveryTransport>());
        builder.Services.AddSingleton<ReliableOrderDeliveryProcessor>();
        builder.Services.AddHostedService<RabbitMqOrderConsumer>();

        await builder.Build().RunAsync();
    }
}
