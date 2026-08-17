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

    public static void Seed(TradeCoreDbContext dbContext)
    {
        foreach (var seedStock in SeedStocks)
        {
            if (dbContext.Stocks.Any(stock =>
                    stock.Symbol.ToUpper() == seedStock.Symbol.ToUpper()))
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
            dbContext.SaveChanges();
        }
    }

    private sealed record SeedStock(string Id, string Symbol, string Name, decimal CurrentPrice);
}
