using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Services;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

/// <summary>Loads a submitted order and sends it through the established processing path.</summary>
public sealed class OrderMessageHandler(IServiceScopeFactory scopeFactory)
    : IOrderMessageHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        OrderSubmittedMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<OrderSubmittedMessage>(body.Span, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOrderMessageException("Submitted-order message is not valid JSON.", exception);
        }

        if (message is null || message.OrderId == Guid.Empty)
        {
            throw new InvalidOrderMessageException("Submitted-order message has no valid order ID.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<TradeCoreDbContext>();
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == message.OrderId,
            cancellationToken);

        if (order is null)
        {
            throw new PersistedOrderNotFoundException(message.OrderId);
        }

        await services.GetRequiredService<OrderProcessingService>().ProcessOrderAsync(order, cancellationToken);
    }
}
