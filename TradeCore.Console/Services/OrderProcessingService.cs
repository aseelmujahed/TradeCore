using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class OrderProcessingService
{
    private readonly TradeCreationService _tradeCreationService;

    public OrderProcessingService(TradeCreationService tradeCreationService)
    {
        _tradeCreationService = tradeCreationService ?? throw new ArgumentNullException(nameof(tradeCreationService));
    }

    public async Task<OrderProcessingResult> ProcessOrderAsync(
        Order submittedOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submittedOrder);

        var trade = await _tradeCreationService.CreateTradeAsync(submittedOrder.StockId, cancellationToken);
        return new OrderProcessingResult(submittedOrder, trade);
    }
}
