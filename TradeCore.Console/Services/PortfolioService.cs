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

    public PortfolioPosition AddPurchasedShares(Guid accountId, string stockSymbol, int quantity)
    {
        _accountService.GetAccount(accountId);
        var stock = _stockService.GetStockBySymbol(stockSymbol);
        var position = _dbContext.PortfolioPositions.SingleOrDefault(
            position => position.AccountId == accountId && position.StockId == stock.Id);

        if (position is not null)
        {
            position.AddShares(quantity, stock.CurrentPrice);
            _dbContext.SaveChanges();
            return position;
        }

        position = new PortfolioPosition(
            Guid.NewGuid(),
            accountId,
            stock.Id,
            quantity,
            stock.CurrentPrice);

        _dbContext.PortfolioPositions.Add(position);
        _dbContext.SaveChanges();

        return position;
    }

    public void SellShares(Guid accountId, string stockSymbol, int quantity)
    {
        _accountService.GetAccount(accountId);
        var stock = _stockService.GetStockBySymbol(stockSymbol);
        var position = _dbContext.PortfolioPositions.SingleOrDefault(
            position => position.AccountId == accountId && position.StockId == stock.Id);

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

        _dbContext.SaveChanges();
    }

    public IReadOnlyList<PortfolioPosition> GetPortfolioPositions(Guid accountId)
    {
        _accountService.GetAccount(accountId);

        return _dbContext.PortfolioPositions
            .Where(position => position.AccountId == accountId)
            .ToList();
    }
}
