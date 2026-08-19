using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.Orders;
using TradeCore.Api.DTOs.Stocks;
using TradeCore.Api.DTOs.Trades;
using TradeCore.Api.Notifications;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;
    private readonly OrderProcessingService _orderProcessingService;
    private readonly ITradeExecutionNotifier _tradeExecutionNotifier;
    private readonly IStockPriceNotifier _stockPriceNotifier;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        OrderService orderService,
        OrderProcessingService orderProcessingService,
        ITradeExecutionNotifier tradeExecutionNotifier,
        IStockPriceNotifier stockPriceNotifier,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _orderProcessingService = orderProcessingService;
        _tradeExecutionNotifier = tradeExecutionNotifier;
        _stockPriceNotifier = stockPriceNotifier;
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

            var processingResult = await _orderProcessingService.ProcessOrderAsync(order, cancellationToken);
            foreach (var trade in processingResult.Trades)
            {
                try
                {
                    await _tradeExecutionNotifier.NotifyTradeExecutedAsync(
                        ToTradeResponse(trade),
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Failed to broadcast TradeExecuted notification for committed trade {TradeId}.",
                        trade.Id);
                }
            }

            if (processingResult.StockPriceUpdate is not null)
            {
                try
                {
                    await _stockPriceNotifier.NotifyStockPriceUpdatedAsync(
                        new StockPriceUpdatedResponse(
                            processingResult.StockPriceUpdate.StockId,
                            processingResult.StockPriceUpdate.Symbol,
                            processingResult.StockPriceUpdate.Price),
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Failed to broadcast StockPriceUpdated notification for committed stock {StockId}.",
                        processingResult.StockPriceUpdate.StockId);
                }
            }

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

    private static TradeResponse ToTradeResponse(Trade trade)
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
