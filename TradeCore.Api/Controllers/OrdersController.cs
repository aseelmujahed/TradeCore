using Microsoft.AspNetCore.Mvc;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public ActionResult<Order> CreateOrder(CreateOrderRequest request)
    {
        try
        {
            var order = _orderService.CreateOrder(
                request.AccountId,
                request.StockSymbol,
                request.Type,
                request.Quantity,
                request.Price);

            return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, order);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<Order>> GetOrders()
    {
        return Ok(_orderService.GetAllOrders());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<Order> GetOrderById(Guid id)
    {
        try
        {
            return Ok(_orderService.GetOrder(id));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

public record CreateOrderRequest(
    Guid AccountId,
    string StockSymbol,
    OrderType Type,
    int Quantity,
    decimal Price);
