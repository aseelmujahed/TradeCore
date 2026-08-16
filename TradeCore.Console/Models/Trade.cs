namespace TradeCore.Console.Models;

public class Trade
{
    public Guid Id { get; private set; }

    public Guid BuyOrderId { get; private set; }

    public Guid SellOrderId { get; private set; }

    public Guid StockId { get; private set; }

    public int Quantity { get; private set; }

    public decimal Price { get; private set; }

    public DateTime ExecutedAt { get; private set; }

    public Trade(Guid id, Guid buyOrderId, Guid sellOrderId, Guid stockId, int quantity, decimal price)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Trade ID cannot be empty.", nameof(id));
        }

        if (buyOrderId == Guid.Empty)
        {
            throw new ArgumentException("Buy order ID cannot be empty.", nameof(buyOrderId));
        }

        if (sellOrderId == Guid.Empty)
        {
            throw new ArgumentException("Sell order ID cannot be empty.", nameof(sellOrderId));
        }

        if (stockId == Guid.Empty)
        {
            throw new ArgumentException("Stock ID cannot be empty.", nameof(stockId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        }

        Id = id;
        BuyOrderId = buyOrderId;
        SellOrderId = sellOrderId;
        StockId = stockId;
        Quantity = quantity;
        Price = price;
        ExecutedAt = DateTime.UtcNow;
    }
}
