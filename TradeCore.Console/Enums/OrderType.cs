using System.Text.Json.Serialization;

namespace TradeCore.Console.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderType
{
    Buy,
    Sell
}
