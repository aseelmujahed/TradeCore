using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class OrderService
{
    private readonly AccountService _accountService;
    private readonly StockService _stockService;
    private readonly TradeCoreDbContext _dbContext;

    public OrderService(
        TradeCoreDbContext dbContext,
        AccountService accountService,
        StockService stockService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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

        _dbContext.Orders.Add(order);
        _dbContext.SaveChanges();

        return order;
    }

    public Order GetOrder(Guid orderId)
    {
        var order = _dbContext.Orders.SingleOrDefault(order => order.Id == orderId);

        if (order is null)
        {
            throw new KeyNotFoundException($"Order with ID '{orderId}' was not found.");
        }

        return order;
    }

    public IReadOnlyList<Order> GetAllOrders()
    {
        return _dbContext.Orders.ToList();
    }
}
