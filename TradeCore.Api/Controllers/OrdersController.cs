using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.Messaging;
using TradeCore.Messaging;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;
    private readonly IOrderMessagePublisher _orderMessagePublisher;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        OrderService orderService,
        IOrderMessagePublisher orderMessagePublisher,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _orderMessagePublisher = orderMessagePublisher;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _orderService.CreateOrderAsync(
                request.AccountId,
                request.StockSymbol.Trim(),
                request.OrderType,
                request.Quantity,
                request.Price,
                cancellationToken);

            await _orderMessagePublisher.PublishAsync(new OrderSubmittedMessage(order.Id), cancellationToken);

            _logger.LogInformation(
                "Order {OrderId} submitted for account {AccountId} and stock {StockId} with type {OrderType}, quantity {Quantity}, and price {Price}.",
                order.Id,
                order.AccountId,
                order.StockId,
                order.Type,
                order.Quantity,
                order.Price);

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
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> GetOrders(CancellationToken cancellationToken)
    {
        var orders = await _orderService.GetAllOrdersAsync(cancellationToken);
        return Ok(orders.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ToResponse(await _orderService.GetOrderAsync(id, cancellationToken)));
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
