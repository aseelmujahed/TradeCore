namespace TradeCore.Console.Models;

public class Trade
{
    public Guid Id { get; set; }

    public Guid BuyOrderId { get; set; }

    public Guid SellOrderId { get; set; }

    public Guid StockId { get; set; }

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime ExecutedAt { get; set; }
}
