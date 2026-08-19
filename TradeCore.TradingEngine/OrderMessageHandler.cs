using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Services;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

/// <summary>Loads a submitted order and sends it through the established processing path.</summary>
public sealed class OrderMessageHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderMessageHandler> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        OrderSubmittedMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<OrderSubmittedMessage>(body.Span, SerializerOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarding malformed submitted-order message.");
            return false;
        }

        if (message is null || message.OrderId == Guid.Empty)
        {
            logger.LogWarning("Discarding submitted-order message with no valid order ID.");
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<TradeCoreDbContext>();
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == message.OrderId,
            cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Submitted-order message references missing order {OrderId}.", message.OrderId);
            return false;
        }

        await services.GetRequiredService<OrderProcessingService>()
            .ProcessOrderAsync(order, cancellationToken);
        return true;
    }
}
