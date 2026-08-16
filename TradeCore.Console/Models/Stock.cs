namespace TradeCore.Console.Models;

public class Stock
{
    public Guid Id { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal CurrentPrice { get; set; }
}
