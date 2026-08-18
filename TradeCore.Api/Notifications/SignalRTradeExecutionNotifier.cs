using Microsoft.AspNetCore.SignalR;
using TradeCore.Api.DTOs.Trades;
using TradeCore.Api.Hubs;

namespace TradeCore.Api.Notifications;

public sealed class SignalRTradeExecutionNotifier(IHubContext<TradingHub> tradingHubContext) : ITradeExecutionNotifier
{
    public Task NotifyTradeExecutedAsync(TradeResponse trade, CancellationToken cancellationToken = default)
    {
        return tradingHubContext.Clients.All.SendAsync("TradeExecuted", trade, cancellationToken);
    }
}
