using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class OrderProcessingService
{
    private readonly TradeCreationService _tradeCreationService;
    private readonly StockProcessingLockRegistry _stockProcessingLocks;

    public OrderProcessingService(
        TradeCreationService tradeCreationService,
        StockProcessingLockRegistry? stockProcessingLocks = null)
    {
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

        var trade = await _tradeCreationService.CreateTradeAsync(submittedOrder.StockId, cancellationToken);
        return new OrderProcessingResult(submittedOrder, trade);
    }
}
