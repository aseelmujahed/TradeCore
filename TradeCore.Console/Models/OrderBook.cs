namespace TradeCore.Console.Models;

public sealed record OrderBook(
    Guid StockId,
    IReadOnlyList<Order> BuyOrders,
    IReadOnlyList<Order> SellOrders);
