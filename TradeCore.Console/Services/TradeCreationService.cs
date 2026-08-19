using Microsoft.EntityFrameworkCore;
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

    public async Task<Trade?> CreateTradeAsync(Guid stockId, CancellationToken cancellationToken = default)
    {
        var orderMatch = await _orderMatchingService.FindBestMatchAsync(stockId, cancellationToken);

        if (orderMatch is null)
        {
            return null;
        }

        var buyOrder = await _dbContext.Orders.SingleAsync(order => order.Id == orderMatch.BuyOrder.Id, cancellationToken);
        var sellOrder = await _dbContext.Orders.SingleAsync(order => order.Id == orderMatch.SellOrder.Id, cancellationToken);
        var stock = await _dbContext.Stocks.SingleAsync(stock => stock.Id == stockId, cancellationToken);
        var buyerAccount = await _dbContext.Accounts.SingleAsync(account => account.Id == buyOrder.AccountId, cancellationToken);
        var sellerAccount = await _dbContext.Accounts.SingleAsync(account => account.Id == sellOrder.AccountId, cancellationToken);
        var tradeValue = orderMatch.MatchPrice * orderMatch.MatchedQuantity;

        if (tradeValue > 0)
        {
            buyerAccount.EnsureCanDebit(tradeValue);
        }

        await _portfolioService.EnsureSufficientSharesAsync(
            sellerAccount.Id,
            stockId,
            orderMatch.MatchedQuantity,
            cancellationToken);

        if (tradeValue > 0)
        {
            buyerAccount.Debit(tradeValue);
            sellerAccount.Deposit(tradeValue);
        }

        await _portfolioService.ApplyTradeSettlementAsync(
            buyerAccount.Id,
            sellerAccount.Id,
            stockId,
            orderMatch.MatchedQuantity,
            orderMatch.MatchPrice,
            cancellationToken);

        buyOrder.ApplyFill(orderMatch.MatchedQuantity);
        sellOrder.ApplyFill(orderMatch.MatchedQuantity);
        var trade = new Trade(
            Guid.NewGuid(),
            orderMatch.BuyOrder.Id,
            orderMatch.SellOrder.Id,
            stockId,
            orderMatch.MatchedQuantity,
            orderMatch.MatchPrice);

        stock.UpdateCurrentPrice(trade.Price);
        _dbContext.Trades.Add(trade);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return trade;
    }
}
