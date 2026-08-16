using TradeCore.Console.Enums;

namespace TradeCore.Api.DTOs.Orders;

public record CreateOrderRequest(
    Guid AccountId,
    string StockSymbol,
    OrderType Type,
    int Quantity,
    decimal Price);
