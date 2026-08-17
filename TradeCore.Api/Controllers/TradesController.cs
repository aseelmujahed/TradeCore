using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.Trades;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/trades")]
public class TradesController : ControllerBase
{
    private readonly TradeHistoryService _tradeHistoryService;

    public TradesController(TradeHistoryService tradeHistoryService)
    {
        _tradeHistoryService = tradeHistoryService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TradeResponse>>> GetTrades(CancellationToken cancellationToken)
    {
        var trades = await _tradeHistoryService.GetAllTradesAsync(cancellationToken);
        return Ok(trades.Select(ToResponse).ToList());
    }

    private static TradeResponse ToResponse(Trade trade)
    {
        return new TradeResponse(
            trade.Id,
            trade.BuyOrderId,
            trade.SellOrderId,
            trade.StockId,
            trade.Quantity,
            trade.Price,
            trade.ExecutedAt);
    }
}
