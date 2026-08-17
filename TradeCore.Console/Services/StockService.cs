using Microsoft.EntityFrameworkCore;
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

    public async Task<Stock> AddStockAsync(string symbol, string name, decimal currentPrice, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Stock symbol cannot be empty.", nameof(symbol));
        }

        if (await _dbContext.Stocks.AnyAsync(stock => stock.Symbol.ToUpper() == symbol.ToUpper(), cancellationToken))
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        return stock;
    }

    public async Task<Stock> GetStockBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Stock symbol cannot be empty.", nameof(symbol));
        }

        var stock = await _dbContext.Stocks.SingleOrDefaultAsync(
            stock => stock.Symbol.ToUpper() == symbol.ToUpper(), cancellationToken);

        if (stock is null)
        {
            throw new KeyNotFoundException(
                $"Stock with symbol '{symbol}' was not found.");
        }

        return stock;
    }

    public async Task<IReadOnlyList<Stock>> GetAllStocksAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Stocks.ToListAsync(cancellationToken);
    }
}
