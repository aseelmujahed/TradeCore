using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class OrderMatchingService
{
    private readonly OrderBookService _orderBookService;

    public OrderMatchingService(OrderBookService orderBookService)
    {
        _orderBookService = orderBookService ?? throw new ArgumentNullException(nameof(orderBookService));
    }

    public async Task<OrderMatch?> FindBestMatchAsync(Guid stockId, CancellationToken cancellationToken = default)
    {
        var orderBook = await _orderBookService.GetOrderBookAsync(stockId, cancellationToken);
        foreach (var buyOrder in orderBook.BuyOrders)
        {
            foreach (var sellOrder in orderBook.SellOrders)
            {
                if (buyOrder.Price < sellOrder.Price)
                {
                    break;
                }

                if (buyOrder.AccountId == sellOrder.AccountId)
                {
                    continue;
                }

                return new OrderMatch(
                    buyOrder,
                    sellOrder,
                    Math.Min(buyOrder.Quantity, sellOrder.Quantity),
                    GetMatchPrice(buyOrder, sellOrder));
            }
        }

        return null;
    }

    private static decimal GetMatchPrice(Order buyOrder, Order sellOrder)
    {
        if (buyOrder.CreatedAt < sellOrder.CreatedAt)
        {
            return buyOrder.Price;
        }

        if (sellOrder.CreatedAt < buyOrder.CreatedAt)
        {
            return sellOrder.Price;
        }

        return buyOrder.Id.CompareTo(sellOrder.Id) < 0
            ? buyOrder.Price
            : sellOrder.Price;
    }
}
