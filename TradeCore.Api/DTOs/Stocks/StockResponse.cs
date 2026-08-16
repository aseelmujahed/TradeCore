namespace TradeCore.Api.DTOs.Stocks;

public record StockResponse(Guid Id, string Symbol, string Name, decimal CurrentPrice);
