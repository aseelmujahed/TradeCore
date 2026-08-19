using Microsoft.AspNetCore.SignalR;
using TradeCore.Api.DTOs.Stocks;
using TradeCore.Api.Hubs;

namespace TradeCore.Api.Notifications;

public sealed class SignalRStockPriceNotifier(IHubContext<TradingHub> tradingHubContext) : IStockPriceNotifier
{
    public Task NotifyStockPriceUpdatedAsync(StockPriceUpdatedResponse update, CancellationToken cancellationToken = default)
    {
        return tradingHubContext.Clients.All.SendAsync("StockPriceUpdated", update, cancellationToken);
    }
}
