using TradeCore.Api.DTOs.Trades;

namespace TradeCore.Api.Notifications;

public interface ITradeExecutionNotifier
{
    Task NotifyTradeExecutedAsync(TradeResponse trade, CancellationToken cancellationToken = default);
}
