using System.ComponentModel.DataAnnotations;
using TradeCore.Api.DTOs.Validation;
using TradeCore.Console.Enums;

namespace TradeCore.Api.DTOs.Orders;

public record CreateOrderRequest(
    [param: NotEmptyGuid]
    Guid AccountId,

    [param: Required(ErrorMessage = "StockSymbol is required.")]
    [param: StringLength(20, ErrorMessage = "StockSymbol must be 20 characters or fewer.")]
    [param: RegularExpression(@".*\S.*", ErrorMessage = "StockSymbol cannot be whitespace only.")]
    string StockSymbol,

    [param: EnumDataType(typeof(OrderType), ErrorMessage = "Type must be Buy or Sell.")]
    OrderType Type,

    [param: Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than 0.")]
    int Quantity,

    [param: Range(
        typeof(decimal),
        "0.0000000000000000000000000001",
        "79228162514264337593543950335",
        ErrorMessage = "Price must be greater than 0.")]
    decimal Price);
