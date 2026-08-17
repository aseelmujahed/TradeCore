using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Api.Data;

public static class StockDataSeeder
{
    private static readonly SeedStock[] SeedStocks =
    [
        new("7c91a50a-f398-42d0-8dc5-4e873f9db101", "AAPL", "Apple Inc.", 180.00m),
        new("7c91a50a-f398-42d0-8dc5-4e873f9db102", "MSFT", "Microsoft Corporation", 420.00m),
        new("7c91a50a-f398-42d0-8dc5-4e873f9db103", "GOOG", "Alphabet Inc.", 175.00m),
        new("7c91a50a-f398-42d0-8dc5-4e873f9db104", "TSLA", "Tesla, Inc.", 250.00m)
    ];

    public static async Task SeedAsync(TradeCoreDbContext dbContext, CancellationToken cancellationToken = default)
    {
        foreach (var seedStock in SeedStocks)
        {
            if (await dbContext.Stocks.AnyAsync(stock =>
                    stock.Symbol.ToUpper() == seedStock.Symbol.ToUpper(), cancellationToken))
            {
                continue;
            }

            dbContext.Stocks.Add(new Stock(
                Guid.Parse(seedStock.Id),
                seedStock.Symbol,
                seedStock.Name,
                seedStock.CurrentPrice));
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record SeedStock(string Id, string Symbol, string Name, decimal CurrentPrice);
}
