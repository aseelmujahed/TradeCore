using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradeCore.Api.Hubs;
using TradeCore.Api.Notifications;
using TradeCore.Api.Data;
using TradeCore.Api.ExceptionHandling;
using TradeCore.Api.Messaging;
using TradeCore.Messaging;
using TradeCore.Console.Data;
using TradeCore.Console.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("TradeCoreDatabase")
    ?? throw new InvalidOperationException(
        "The required 'ConnectionStrings:TradeCoreDatabase' configuration setting is missing.");

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddDbContext<TradeCoreDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(Program).Assembly.GetName().Name)));
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(allowIntegerValues: false));
    });
builder.Services.AddSignalR();
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>();
builder.Services.AddSingleton<IRabbitMqClientFactory, RabbitMqClientFactory>();
builder.Services.AddSingleton<RabbitMqConnectionService>();
builder.Services.AddSingleton<IRabbitMqConnectionService>(serviceProvider =>
    serviceProvider.GetRequiredService<RabbitMqConnectionService>());
builder.Services.AddHostedService<RabbitMqInitializationService>();
builder.Services.AddHostedService<RabbitMqTradingEventConsumer>();
builder.Services.AddScoped<IOrderMessagePublisher, RabbitMqOrderMessagePublisher>();
builder.Services.AddScoped<ITradeExecutionNotifier, SignalRTradeExecutionNotifier>();
builder.Services.AddScoped<IStockPriceNotifier, SignalRStockPriceNotifier>();
builder.Services.AddScoped<TradingEventNotificationHandler>();
builder.Services.AddSingleton<TradingEventDeduplicator>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderBookService>();
builder.Services.AddScoped<OrderMatchingService>();
builder.Services.AddScoped<TradeCreationService>();
builder.Services.AddSingleton<StockProcessingLockRegistry>();
builder.Services.AddScoped<OrderProcessingService>();
builder.Services.AddScoped<TradeHistoryService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<PortfolioTransferService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TradeCoreDbContext>();
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        await dbContext.Database.MigrateAsync();
    }

    await StockDataSeeder.SeedAsync(dbContext);
}

if (app.Configuration.GetValue<bool>("Database:ExitAfterMigration"))
{
    return;
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "TradeCore API v1");
    });
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapHub<TradingHub>("/hubs/trading");
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
