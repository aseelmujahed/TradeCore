using Microsoft.AspNetCore.Mvc;
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
    public ActionResult<IReadOnlyList<Stock>> GetStocks()
    {
        return Ok(_stockService.GetAllStocks());
    }

    [HttpGet("{symbol}")]
    public ActionResult<Stock> GetStockBySymbol(string symbol)
    {
        try
        {
            return Ok(_stockService.GetStockBySymbol(symbol.Trim()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
