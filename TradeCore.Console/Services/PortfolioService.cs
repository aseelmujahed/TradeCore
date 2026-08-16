using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class PortfolioService
{
    private readonly AccountService _accountService;
    private readonly StockService _stockService;
    private readonly Dictionary<(Guid AccountId, Guid StockId), PortfolioPosition> _positions = new();

    public PortfolioService(AccountService accountService, StockService stockService)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
    }

    public PortfolioPosition AddPurchasedShares(Guid accountId, string stockSymbol, int quantity)
    {
        _accountService.GetAccount(accountId);
        var stock = _stockService.GetStockBySymbol(stockSymbol);
        var key = (accountId, stock.Id);

        if (_positions.TryGetValue(key, out var position))
        {
            position.AddShares(quantity, stock.CurrentPrice);
            return position;
        }

        position = new PortfolioPosition(
            Guid.NewGuid(),
            accountId,
            stock.Id,
            quantity,
            stock.CurrentPrice);

        _positions.Add(key, position);

        return position;
    }

    public void SellShares(Guid accountId, string stockSymbol, int quantity)
    {
        _accountService.GetAccount(accountId);
        var stock = _stockService.GetStockBySymbol(stockSymbol);
        var key = (accountId, stock.Id);

        if (!_positions.TryGetValue(key, out var position))
        {
            throw new KeyNotFoundException(
                $"Portfolio position for account '{accountId}' and stock '{stock.Symbol}' was not found.");
        }

        position.RemoveShares(quantity);

        if (position.Quantity == 0)
        {
            _positions.Remove(key);
        }
    }

    public IReadOnlyList<PortfolioPosition> GetPortfolioPositions(Guid accountId)
    {
        _accountService.GetAccount(accountId);

        return _positions.Values
            .Where(position => position.AccountId == accountId)
            .ToList();
    }
}
