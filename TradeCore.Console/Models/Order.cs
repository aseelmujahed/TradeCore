namespace TradeCore.Console.Models;

public class Order
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid StockId { get; set; }

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime CreatedAt { get; set; }
}
