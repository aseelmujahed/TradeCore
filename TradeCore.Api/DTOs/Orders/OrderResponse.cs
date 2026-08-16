using TradeCore.Console.Enums;

namespace TradeCore.Api.DTOs.Orders;

public record OrderResponse(
    Guid Id,
    Guid AccountId,
    Guid StockId,
    OrderType Type,
    int Quantity,
    decimal Price,
    OrderStatus Status,
    DateTime CreatedAt);
