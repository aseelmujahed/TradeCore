namespace TradeCore.Console.Services;

public sealed record StockPriceUpdate(Guid StockId, string Symbol, decimal Price);
