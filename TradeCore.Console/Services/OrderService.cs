using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Messaging;

namespace TradeCore.Console.Services;

public class OrderService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
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

    public async Task<(Order Order, OutboxMessage OutboxMessage)> CreateOrderWithOutboxAsync(
        Guid accountId,
        string stockSymbol,
        OrderType type,
        int quantity,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        await _accountService.GetAccountAsync(accountId, cancellationToken);
        var stock = await _stockService.GetStockBySymbolAsync(stockSymbol, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = new Order(Guid.NewGuid(), accountId, stock.Id, type, quantity, price);
            var outboxMessage = new OutboxMessage(
                Guid.NewGuid(),
                order.Id,
                OutboxMessage.OrderSubmittedMessageType,
                JsonSerializer.Serialize(new OrderSubmittedMessage(order.Id), SerializerOptions));

            _dbContext.Orders.Add(order);
            _dbContext.OutboxMessages.Add(outboxMessage);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (order, outboxMessage);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
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
