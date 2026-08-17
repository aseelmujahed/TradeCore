using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.Orders;
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
    public ActionResult<OrderResponse> CreateOrder(CreateOrderRequest request)
    {
        try
        {
            var order = _orderService.CreateOrder(
                request.AccountId,
                request.StockSymbol.Trim(),
                request.Type,
                request.Quantity,
                request.Price);

            return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, ToResponse(order));
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
    public ActionResult<IReadOnlyList<OrderResponse>> GetOrders()
    {
        return Ok(_orderService.GetAllOrders().Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<OrderResponse> GetOrderById(Guid id)
    {
        try
        {
            return Ok(ToResponse(_orderService.GetOrder(id)));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static OrderResponse ToResponse(Order order)
    {
        return new OrderResponse(
            order.Id,
            order.AccountId,
            order.StockId,
            order.Type,
            order.Quantity,
            order.Price,
            order.Status,
            order.CreatedAt);
    }
}
