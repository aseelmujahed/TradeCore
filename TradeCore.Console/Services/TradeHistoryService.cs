using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class TradeHistoryService
{
    private readonly TradeCoreDbContext _dbContext;

    public TradeHistoryService(TradeCoreDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public IReadOnlyList<Trade> GetAllTrades()
    {
        return _dbContext.Trades
            .AsNoTracking()
            .OrderByDescending(trade => trade.ExecutedAt)
            .ThenByDescending(trade => trade.Id)
            .ToList();
    }
}
