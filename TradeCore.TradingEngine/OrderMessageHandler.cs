using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Services;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

/// <summary>Loads a submitted order and sends it through the established processing path.</summary>
public sealed class OrderMessageHandler(
    IServiceScopeFactory scopeFactory,
    ITradingEventPublisher? tradingEventPublisher = null)
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

        // Re-querying makes a redelivered order message safe: after a publish failure the
        // committed trade can be emitted again with the same deterministic event ID.
        var trades = await dbContext.Trades
            .AsNoTracking()
            .Where(trade => trade.BuyOrderId == message.OrderId || trade.SellOrderId == message.OrderId)
            .OrderBy(trade => trade.ExecutedAt)
            .ToListAsync(cancellationToken);
        if (trades.Count == 0)
        {
            return;
        }

        var stock = await dbContext.Stocks
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == order.StockId, cancellationToken);
        var publisher = tradingEventPublisher ?? NullTradingEventPublisher.Instance;
        foreach (var trade in trades)
        {
            await publisher.PublishAsync(
                new TradeExecutedEvent(
                    trade.Id,
                    trade.Id,
                    trade.BuyOrderId,
                    trade.SellOrderId,
                    trade.StockId,
                    trade.Quantity,
                    trade.Price,
                    trade.ExecutedAt),
                cancellationToken);
        }

        var mostRecentTrade = trades[^1];
        await publisher.PublishAsync(
            new StockPriceUpdatedEvent(
                mostRecentTrade.Id,
                stock.Id,
                stock.Symbol,
                mostRecentTrade.Price),
            cancellationToken);
    }

    private sealed class NullTradingEventPublisher : ITradingEventPublisher
    {
        public static readonly NullTradingEventPublisher Instance = new();

        public Task PublishAsync(TradeExecutedEvent message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(StockPriceUpdatedEvent message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
