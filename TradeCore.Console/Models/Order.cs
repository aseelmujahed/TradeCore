using TradeCore.Console.Enums;

namespace TradeCore.Console.Models;

public class Order
{
    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid StockId { get; private set; }

    public OrderType Type { get; private set; }

    public OrderStatus Status { get; private set; }

    public int Quantity { get; private set; }

    public decimal Price { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public Order(Guid id, Guid accountId, Guid stockId, OrderType type, int quantity, decimal price)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account ID cannot be empty.", nameof(accountId));
        }

        if (stockId == Guid.Empty)
        {
            throw new ArgumentException("Stock ID cannot be empty.", nameof(stockId));
        }

        if (type != OrderType.Buy && type != OrderType.Sell)
        {
            throw new ArgumentException("Order type must be Buy or Sell.", nameof(type));
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
        AccountId = accountId;
        StockId = stockId;
        Type = type;
        Status = OrderStatus.Pending;
        Quantity = quantity;
        Price = price;
        CreatedAt = DateTime.UtcNow;
    }
}
