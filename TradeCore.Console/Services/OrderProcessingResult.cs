using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed record OrderProcessingResult(Order SubmittedOrder, IReadOnlyList<Trade> Trades)
{
    public Trade? Trade => Trades.FirstOrDefault();

    public bool HasTrade => Trades.Count != 0;
}
