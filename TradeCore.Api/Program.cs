using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TradeCore.Api.Hubs;
using TradeCore.Api.Notifications;
using TradeCore.Api.Data;
using TradeCore.Api.ExceptionHandling;
using TradeCore.Console.Data;
using TradeCore.Console.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("TradeCoreDatabase")
    ?? throw new InvalidOperationException(
        "The required 'ConnectionStrings:TradeCoreDatabase' configuration setting is missing.");

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
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
builder.Services.AddScoped<ITradeExecutionNotifier, SignalRTradeExecutionNotifier>();
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
    await StockDataSeeder.SeedAsync(dbContext);
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

app.Run();

public partial class Program;
