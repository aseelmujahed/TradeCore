using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class TradeCreationService
{
    private readonly TradeCoreDbContext _dbContext;
    private readonly OrderMatchingService _orderMatchingService;

    public TradeCreationService(
        TradeCoreDbContext dbContext,
        OrderMatchingService orderMatchingService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _orderMatchingService = orderMatchingService ?? throw new ArgumentNullException(nameof(orderMatchingService));
    }

    public Trade? CreateTrade(Guid stockId)
    {
        var orderMatch = _orderMatchingService.FindBestMatch(stockId);

        if (orderMatch is null)
        {
            return null;
        }

        var buyOrder = _dbContext.Orders.Single(order => order.Id == orderMatch.BuyOrder.Id);
        var sellOrder = _dbContext.Orders.Single(order => order.Id == orderMatch.SellOrder.Id);

        buyOrder.ApplyFill(orderMatch.MatchedQuantity);
        sellOrder.ApplyFill(orderMatch.MatchedQuantity);
        _dbContext.SaveChanges();

        return new Trade(
            Guid.NewGuid(),
            orderMatch.BuyOrder.Id,
            orderMatch.SellOrder.Id,
            stockId,
            orderMatch.MatchedQuantity,
            orderMatch.MatchPrice);
    }
}
