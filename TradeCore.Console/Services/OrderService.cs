using Microsoft.EntityFrameworkCore;
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

    public async Task<Order> CreateOrderAsync(
        Guid accountId,
        string stockSymbol,
        OrderType type,
        int quantity,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        await _accountService.GetAccountAsync(accountId, cancellationToken);
        var stock = await _stockService.GetStockBySymbolAsync(stockSymbol, cancellationToken);

        var order = new Order(
            Guid.NewGuid(),
            accountId,
            stock.Id,
            type,
            quantity,
            price);

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return order;
    }

    public async Task<Order> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders.SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new KeyNotFoundException($"Order with ID '{orderId}' was not found.");
        }

        return order;
    }

    public async Task<IReadOnlyList<Order>> GetAllOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders.ToListAsync(cancellationToken);
    }
}
