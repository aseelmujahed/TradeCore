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

    /// <summary>
    /// Set atomically with the first successful handling of this order's submitted-order message.
    /// It makes broker redelivery a durable no-op, including when the first handling found no match.
    /// </summary>
    public DateTime? SubmittedMessageProcessedAt { get; private set; }

    private Order()
    {
    }

    public Order(Guid id, Guid accountId, Guid stockId, OrderType type, int initialQuantity, decimal price)
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

        if (initialQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialQuantity), "Quantity must be greater than zero.");
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
        Quantity = initialQuantity;
        Price = price;
        CreatedAt = DateTime.UtcNow;
    }

    public void ApplyFill(int executedQuantity)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.PartiallyFilled))
        {
            throw new InvalidOperationException("Only active orders can be filled.");
        }

        if (executedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executedQuantity),
                "Executed quantity must be greater than zero.");
        }

        if (executedQuantity > Quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executedQuantity),
                "Executed quantity cannot exceed the remaining order quantity.");
        }

        Quantity -= executedQuantity;
        Status = Quantity == 0
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;
    }
}
