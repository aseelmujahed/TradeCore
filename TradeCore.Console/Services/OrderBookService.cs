using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class OrderBookService
{
    private readonly TradeCoreDbContext _dbContext;

    public OrderBookService(TradeCoreDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public OrderBook GetOrderBook(Guid stockId)
    {
        if (stockId == Guid.Empty)
        {
            throw new ArgumentException("Stock ID cannot be empty.", nameof(stockId));
        }

        var activeOrders = _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.StockId == stockId &&
                (order.Status == OrderStatus.Pending || order.Status == OrderStatus.PartiallyFilled));

        var buyOrders = activeOrders
            .Where(order => order.Type == OrderType.Buy)
            .OrderByDescending(order => order.Price)
            .ThenBy(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .ToList();

        var sellOrders = activeOrders
            .Where(order => order.Type == OrderType.Sell)
            .OrderBy(order => order.Price)
            .ThenBy(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .ToList();

        return new OrderBook(stockId, buyOrders, sellOrders);
    }
}
