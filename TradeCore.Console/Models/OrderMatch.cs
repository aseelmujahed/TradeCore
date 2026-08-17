namespace TradeCore.Console.Models;

public sealed record OrderMatch(
    Order BuyOrder,
    Order SellOrder,
    int MatchedQuantity,
    decimal MatchPrice);
