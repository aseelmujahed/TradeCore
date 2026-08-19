using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class OrderProcessingService
{
    private readonly TradeCreationService _tradeCreationService;
    private readonly StockProcessingLockRegistry _stockProcessingLocks;
    private readonly TradeCoreDbContext _dbContext;

    public OrderProcessingService(
        TradeCoreDbContext dbContext,
        TradeCreationService tradeCreationService,
        StockProcessingLockRegistry? stockProcessingLocks = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tradeCreationService = tradeCreationService ?? throw new ArgumentNullException(nameof(tradeCreationService));
        _stockProcessingLocks = stockProcessingLocks ?? new StockProcessingLockRegistry();
    }

    public async Task<OrderProcessingResult> ProcessOrderAsync(
        Order submittedOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submittedOrder);

        await using var processingLock = await _stockProcessingLocks.AcquireAsync(
            submittedOrder.StockId,
            cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var trades = new List<Trade>();

        while (submittedOrder.Status != OrderStatus.Filled)
        {
            var trade = await _tradeCreationService.CreateTradeAsync(submittedOrder.StockId, cancellationToken);
            if (trade is null)
            {
                break;
            }

            trades.Add(trade);
        }

        StockPriceUpdate? stockPriceUpdate = null;
        if (trades.Count > 0)
        {
            var stock = await _dbContext.Stocks.SingleAsync(stock => stock.Id == submittedOrder.StockId, cancellationToken);
            stockPriceUpdate = new StockPriceUpdate(stock.Id, stock.Symbol, stock.CurrentPrice);
        }

        await transaction.CommitAsync(cancellationToken);
        return new OrderProcessingResult(submittedOrder, trades, stockPriceUpdate);
    }
}
