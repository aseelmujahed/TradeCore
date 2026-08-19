using TradeCore.Api.DTOs.Stocks;
using TradeCore.Api.DTOs.Trades;
using TradeCore.Api.Notifications;
using TradeCore.Messaging;

namespace TradeCore.Api.Messaging;

/// <summary>Maps shared integration events to the existing public SignalR payloads.</summary>
public sealed class TradingEventNotificationHandler(
    ITradeExecutionNotifier tradeExecutionNotifier,
    IStockPriceNotifier stockPriceNotifier)
{
    public Task HandleAsync(TradeExecutedEvent message, CancellationToken cancellationToken) =>
        tradeExecutionNotifier.NotifyTradeExecutedAsync(
            new TradeResponse(
                message.TradeId,
                message.BuyOrderId,
                message.SellOrderId,
                message.StockId,
                message.Quantity,
                message.Price,
                message.ExecutedAt),
            cancellationToken);

    public Task HandleAsync(StockPriceUpdatedEvent message, CancellationToken cancellationToken) =>
        stockPriceNotifier.NotifyStockPriceUpdatedAsync(
            new StockPriceUpdatedResponse(message.StockId, message.Symbol, message.Price),
            cancellationToken);
}
