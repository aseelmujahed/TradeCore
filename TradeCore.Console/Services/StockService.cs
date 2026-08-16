using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class StockService
{
    private readonly Dictionary<string, Stock> _stocks = new(StringComparer.OrdinalIgnoreCase);

    public Stock AddStock(string symbol, string name, decimal currentPrice)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Stock symbol cannot be empty.", nameof(symbol));
        }

        if (_stocks.ContainsKey(symbol))
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

        _stocks.Add(stock.Symbol, stock);

        return stock;
    }

    public Stock GetStockBySymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Stock symbol cannot be empty.", nameof(symbol));
        }

        if (!_stocks.TryGetValue(symbol, out var stock))
        {
            throw new KeyNotFoundException(
                $"Stock with symbol '{symbol}' was not found.");
        }

        return stock;
    }

    public IReadOnlyList<Stock> GetAllStocks()
    {
        return _stocks.Values.ToList();
    }
}