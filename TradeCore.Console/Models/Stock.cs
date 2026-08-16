namespace TradeCore.Console.Models;

public class Stock
{
    public Guid Id { get; private set; }

    public string Symbol { get; private set; }

    public string Name { get; private set; }

    public decimal CurrentPrice { get; private set; }

    public Stock(Guid id, string symbol, string name, decimal currentPrice)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Stock ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Stock symbol cannot be empty.", nameof(symbol));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Stock name cannot be empty.", nameof(name));
        }

        if (currentPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPrice), "Current price cannot be negative.");
        }

        Id = id;
        Symbol = symbol;
        Name = name;
        CurrentPrice = currentPrice;
    }
}
