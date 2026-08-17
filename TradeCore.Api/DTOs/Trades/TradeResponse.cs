namespace TradeCore.Api.DTOs.Trades;

public record TradeResponse(
    Guid Id,
    Guid BuyOrderId,
    Guid SellOrderId,
    Guid StockId,
    int Quantity,
    decimal Price,
    DateTime ExecutedAt);
