namespace TradeCore.Api.DTOs.Portfolio;

public record PortfolioPositionResponse(
    Guid AccountId,
    Guid StockId,
    int Quantity,
    decimal AveragePrice);
