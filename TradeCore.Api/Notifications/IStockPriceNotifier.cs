using TradeCore.Api.DTOs.Stocks;

namespace TradeCore.Api.Notifications;

public interface IStockPriceNotifier
{
    Task NotifyStockPriceUpdatedAsync(StockPriceUpdatedResponse update, CancellationToken cancellationToken = default);
}
