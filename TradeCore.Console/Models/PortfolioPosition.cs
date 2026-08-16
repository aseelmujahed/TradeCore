namespace TradeCore.Console.Models;

public class PortfolioPosition
{
    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid StockId { get; private set; }

    public int Quantity { get; private set; }

    public decimal AveragePrice { get; private set; }

    public PortfolioPosition(Guid id, Guid accountId, Guid stockId, int quantity, decimal averagePrice)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Portfolio position ID cannot be empty.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account ID cannot be empty.", nameof(accountId));
        }

        if (stockId == Guid.Empty)
        {
            throw new ArgumentException("Stock ID cannot be empty.", nameof(stockId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (averagePrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(averagePrice), "Average price cannot be negative.");
        }

        Id = id;
        AccountId = accountId;
        StockId = stockId;
        Quantity = quantity;
        AveragePrice = averagePrice;
    }
}
