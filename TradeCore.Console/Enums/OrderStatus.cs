using System.Text.Json.Serialization;

namespace TradeCore.Console.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderStatus
{
    Pending,
    Filled,
    PartiallyFilled,
    Cancelled
}
