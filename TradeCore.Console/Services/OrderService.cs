using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class OrderService
{
    private readonly AccountService _accountService;
    private readonly StockService _stockService;
    private readonly Dictionary<Guid, Order> _orders = new();

    public OrderService(AccountService accountService, StockService stockService)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
    }

    public Order CreateOrder(
        Guid accountId,
        string stockSymbol,
        OrderType type,
        int quantity,
        decimal price)
    {
        _accountService.GetAccount(accountId);
        var stock = _stockService.GetStockBySymbol(stockSymbol);

        var order = new Order(
            Guid.NewGuid(),
            accountId,
            stock.Id,
            type,
            quantity,
            price);

        _orders.Add(order.Id, order);

        return order;
    }

    public Order GetOrder(Guid orderId)
    {
        if (!_orders.TryGetValue(orderId, out var order))
        {
            throw new KeyNotFoundException($"Order with ID '{orderId}' was not found.");
        }

        return order;
    }

    public IReadOnlyList<Order> GetAllOrders()
    {
        return _orders.Values.ToList();
    }
}
