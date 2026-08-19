namespace TradeCore.Api.DTOs.Stocks;

public sealed record StockPriceUpdatedResponse(Guid StockId, string Symbol, decimal Price);
