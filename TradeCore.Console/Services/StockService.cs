using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class StockService
{
    private readonly TradeCoreDbContext _dbContext;

    public StockService(TradeCoreDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Stock AddStock(string symbol, string name, decimal currentPrice)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Stock symbol cannot be empty.", nameof(symbol));
        }

        if (_dbContext.Stocks.Any(stock => stock.Symbol.ToUpper() == symbol.ToUpper()))
        {
            throw new ArgumentException(
                $"A stock with symbol '{symbol}' already exists.",
                nameof(symbol));
        }

        var stock = new Stock(
            Guid.NewGuid(),
            symbol,
            name,
            currentPrice
        );

        _dbContext.Stocks.Add(stock);
        _dbContext.SaveChanges();

        return stock;
    }

    public Stock GetStockBySymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Stock symbol cannot be empty.", nameof(symbol));
        }

        var stock = _dbContext.Stocks.SingleOrDefault(
            stock => stock.Symbol.ToUpper() == symbol.ToUpper());

        if (stock is null)
        {
            throw new KeyNotFoundException(
                $"Stock with symbol '{symbol}' was not found.");
        }

        return stock;
    }

    public IReadOnlyList<Stock> GetAllStocks()
    {
        return _dbContext.Stocks.ToList();
    }
}
