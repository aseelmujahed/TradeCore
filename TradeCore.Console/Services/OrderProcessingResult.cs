using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed record OrderProcessingResult(Order SubmittedOrder, Trade? Trade)
{
    public bool HasTrade => Trade is not null;
}
