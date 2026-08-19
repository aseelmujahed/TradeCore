namespace TradeCore.Messaging;

/// <summary>Shared post-settlement events; they deliberately contain no API or SignalR types.</summary>
public sealed record TradeExecutedEvent(
    Guid EventId,
    Guid TradeId,
    Guid BuyOrderId,
    Guid SellOrderId,
    Guid StockId,
    int Quantity,
    decimal Price,
    DateTime ExecutedAt);

public sealed record StockPriceUpdatedEvent(
    Guid EventId,
    Guid StockId,
    string Symbol,
    decimal Price);

public interface ITradingEventPublisher
{
    Task PublishAsync(TradeExecutedEvent message, CancellationToken cancellationToken);

    Task PublishAsync(StockPriceUpdatedEvent message, CancellationToken cancellationToken);
}
