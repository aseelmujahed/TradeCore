using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class TradeCreationService
{
    private readonly TradeCoreDbContext _dbContext;
    private readonly OrderMatchingService _orderMatchingService;
    private readonly PortfolioService _portfolioService;

    public TradeCreationService(
        TradeCoreDbContext dbContext,
        OrderMatchingService orderMatchingService,
        PortfolioService portfolioService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _orderMatchingService = orderMatchingService ?? throw new ArgumentNullException(nameof(orderMatchingService));
        _portfolioService = portfolioService ?? throw new ArgumentNullException(nameof(portfolioService));
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
        var buyerAccount = _dbContext.Accounts.Single(account => account.Id == buyOrder.AccountId);
        var sellerAccount = _dbContext.Accounts.Single(account => account.Id == sellOrder.AccountId);
        var tradeValue = orderMatch.MatchPrice * orderMatch.MatchedQuantity;

        if (tradeValue > 0)
        {
            buyerAccount.EnsureCanDebit(tradeValue);
        }

        _portfolioService.EnsureSufficientShares(
            sellerAccount.Id,
            stockId,
            orderMatch.MatchedQuantity);

        if (tradeValue > 0)
        {
            buyerAccount.Debit(tradeValue);
            sellerAccount.Deposit(tradeValue);
        }

        _portfolioService.ApplyTradeSettlement(
            buyerAccount.Id,
            sellerAccount.Id,
            stockId,
            orderMatch.MatchedQuantity,
            orderMatch.MatchPrice);

        buyOrder.ApplyFill(orderMatch.MatchedQuantity);
        sellOrder.ApplyFill(orderMatch.MatchedQuantity);
        var trade = new Trade(
            Guid.NewGuid(),
            orderMatch.BuyOrder.Id,
            orderMatch.SellOrder.Id,
            stockId,
            orderMatch.MatchedQuantity,
            orderMatch.MatchPrice);

        _dbContext.Trades.Add(trade);
        _dbContext.SaveChanges();

        return trade;
    }
}
