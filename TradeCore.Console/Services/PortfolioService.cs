using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class PortfolioService
{
    private readonly AccountService _accountService;
    private readonly StockService _stockService;
    private readonly TradeCoreDbContext _dbContext;

    public PortfolioService(
        TradeCoreDbContext dbContext,
        AccountService accountService,
        StockService stockService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
    }

    public async Task<PortfolioPosition> AddPurchasedSharesAsync(
        Guid accountId,
        string stockSymbol,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        await _accountService.GetAccountAsync(accountId, cancellationToken);
        var stock = await _stockService.GetStockBySymbolAsync(stockSymbol, cancellationToken);
        var position = await _dbContext.PortfolioPositions.SingleOrDefaultAsync(
            position => position.AccountId == accountId && position.StockId == stock.Id,
            cancellationToken);

        if (position is not null)
        {
            position.AddShares(quantity, stock.CurrentPrice);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return position;
        }

        position = new PortfolioPosition(
            Guid.NewGuid(),
            accountId,
            stock.Id,
            quantity,
            stock.CurrentPrice);

        _dbContext.PortfolioPositions.Add(position);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return position;
    }

    public async Task SellSharesAsync(
        Guid accountId,
        string stockSymbol,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        await _accountService.GetAccountAsync(accountId, cancellationToken);
        var stock = await _stockService.GetStockBySymbolAsync(stockSymbol, cancellationToken);
        var position = await _dbContext.PortfolioPositions.SingleOrDefaultAsync(
            position => position.AccountId == accountId && position.StockId == stock.Id,
            cancellationToken);

        if (position is null)
        {
            throw new KeyNotFoundException(
                $"Portfolio position for account '{accountId}' and stock '{stock.Symbol}' was not found.");
        }

        position.RemoveShares(quantity);

        if (position.Quantity == 0)
        {
            _dbContext.PortfolioPositions.Remove(position);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddExternalSharesAsync(
        Guid accountId,
        Guid stockId,
        int quantity,
        decimal averagePrice,
        CancellationToken cancellationToken = default)
    {
        var position = await _dbContext.PortfolioPositions.SingleOrDefaultAsync(
            position => position.AccountId == accountId && position.StockId == stockId,
            cancellationToken);

        if (position is null)
        {
            _dbContext.PortfolioPositions.Add(new PortfolioPosition(
                Guid.NewGuid(), accountId, stockId, quantity, averagePrice));
            return;
        }

        position.AddShares(quantity, averagePrice);
    }

    public async Task<IReadOnlyList<PortfolioPosition>> GetPortfolioPositionsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await _accountService.GetAccountAsync(accountId, cancellationToken);

        return await _dbContext.PortfolioPositions
            .Where(position => position.AccountId == accountId)
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureSufficientSharesAsync(
        Guid accountId,
        Guid stockId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var position = await _dbContext.PortfolioPositions.SingleOrDefaultAsync(
            position => position.AccountId == accountId && position.StockId == stockId,
            cancellationToken);

        if (position is null)
        {
            throw new KeyNotFoundException(
                $"Portfolio position for account '{accountId}' and stock '{stockId}' was not found.");
        }

        if (position.Quantity < quantity)
        {
            throw new InvalidOperationException("Insufficient portfolio shares for this trade.");
        }
    }

    public async Task ApplyTradeSettlementAsync(
        Guid buyerAccountId,
        Guid sellerAccountId,
        Guid stockId,
        int quantity,
        decimal purchasePrice,
        CancellationToken cancellationToken = default)
    {
        await EnsureSufficientSharesAsync(sellerAccountId, stockId, quantity, cancellationToken);

        var buyerPosition = await _dbContext.PortfolioPositions.SingleOrDefaultAsync(
            position => position.AccountId == buyerAccountId && position.StockId == stockId,
            cancellationToken);
        var sellerPosition = await _dbContext.PortfolioPositions.SingleAsync(
            position => position.AccountId == sellerAccountId && position.StockId == stockId,
            cancellationToken);

        if (buyerPosition is null)
        {
            buyerPosition = new PortfolioPosition(
                Guid.NewGuid(),
                buyerAccountId,
                stockId,
                quantity,
                purchasePrice);
            _dbContext.PortfolioPositions.Add(buyerPosition);
        }
        else
        {
            buyerPosition.AddShares(quantity, purchasePrice);
        }

        sellerPosition.RemoveShares(quantity);

        if (sellerPosition.Quantity == 0)
        {
            _dbContext.PortfolioPositions.Remove(sellerPosition);
        }
    }
}
