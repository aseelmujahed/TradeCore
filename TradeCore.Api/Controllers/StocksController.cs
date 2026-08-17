using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.Stocks;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/stocks")]
public class StocksController : ControllerBase
{
    private readonly StockService _stockService;

    public StocksController(StockService stockService)
    {
        _stockService = stockService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StockResponse>>> GetStocks(CancellationToken cancellationToken)
    {
        var stocks = await _stockService.GetAllStocksAsync(cancellationToken);
        return Ok(stocks.Select(ToResponse).ToList());
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<StockResponse>> GetStockBySymbol(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ToResponse(await _stockService.GetStockBySymbolAsync(symbol.Trim(), cancellationToken)));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static StockResponse ToResponse(Stock stock)
    {
        return new StockResponse(stock.Id, stock.Symbol, stock.Name, stock.CurrentPrice);
    }
}
