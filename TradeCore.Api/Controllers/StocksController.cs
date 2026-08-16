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
    public ActionResult<IReadOnlyList<StockResponse>> GetStocks()
    {
        return Ok(_stockService.GetAllStocks().Select(ToResponse).ToList());
    }

    [HttpGet("{symbol}")]
    public ActionResult<StockResponse> GetStockBySymbol(string symbol)
    {
        try
        {
            return Ok(ToResponse(_stockService.GetStockBySymbol(symbol.Trim())));
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
