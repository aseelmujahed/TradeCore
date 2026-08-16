namespace TradeCore.Console.Models;

public class PortfolioPosition
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid StockId { get; set; }

    public int Quantity { get; set; }

    public decimal AveragePrice { get; set; }
}
