using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCore.Console.Data;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Messaging;

namespace TradeCore.Console.Services;

public sealed class OrderProcessingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly TradeCreationService _tradeCreationService;
    private readonly StockProcessingLockRegistry _stockProcessingLocks;
    private readonly TradeCoreDbContext _dbContext;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        TradeCoreDbContext dbContext,
        TradeCreationService tradeCreationService,
        StockProcessingLockRegistry? stockProcessingLocks = null)
        : this(dbContext, tradeCreationService, stockProcessingLocks, NullLogger<OrderProcessingService>.Instance)
    {
    }

    public OrderProcessingService(
        TradeCoreDbContext dbContext,
        TradeCreationService tradeCreationService,
        StockProcessingLockRegistry? stockProcessingLocks,
        ILogger<OrderProcessingService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tradeCreationService = tradeCreationService ?? throw new ArgumentNullException(nameof(tradeCreationService));
        _stockProcessingLocks = stockProcessingLocks ?? new StockProcessingLockRegistry();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OrderProcessingResult> ProcessOrderAsync(
        Order submittedOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submittedOrder);

        await using var processingLock = await _stockProcessingLocks.AcquireAsync(
            submittedOrder.StockId,
            cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var trades = new List<Trade>();

        // The claim is conditional in the database, rather than held in memory, so a
        // redelivery after a restart (or from another engine instance) is a no-op.
        // It is part of this transaction and is therefore rolled back with any failed
        // match or settlement.
        var claimed = await _dbContext.Orders
            .Where(order => order.Id == submittedOrder.Id && order.SubmittedMessageProcessedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    order => order.SubmittedMessageProcessedAt,
                    DateTime.UtcNow),
                cancellationToken);

        if (claimed == 0)
        {
            return new OrderProcessingResult(submittedOrder, trades, null);
        }

        // ExecuteUpdate deliberately bypasses EF tracking; reload so the returned
        // result and the active-order check reflect the durable claim and latest state.
        await _dbContext.Entry(submittedOrder).ReloadAsync(cancellationToken);

        while (submittedOrder.Status is OrderStatus.Pending or OrderStatus.PartiallyFilled)
        {
            var trade = await _tradeCreationService.CreateTradeAsync(submittedOrder.StockId, cancellationToken);
            if (trade is null)
            {
                break;
            }

            trades.Add(trade);
        }

        StockPriceUpdate? stockPriceUpdate = null;
        if (trades.Count > 0)
        {
            var stock = await _dbContext.Stocks.SingleAsync(stock => stock.Id == submittedOrder.StockId, cancellationToken);
            stockPriceUpdate = new StockPriceUpdate(stock.Id, stock.Symbol, stock.CurrentPrice);
            AddTradingEventOutboxMessages(submittedOrder, trades, stockPriceUpdate);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        LogCommittedTrades(trades);
        return new OrderProcessingResult(submittedOrder, trades, stockPriceUpdate);
    }

    private void AddTradingEventOutboxMessages(
        Order submittedOrder,
        IReadOnlyList<Trade> trades,
        StockPriceUpdate stockPriceUpdate)
    {
        foreach (var trade in trades)
        {
            var tradeExecuted = new TradeExecutedEvent(
                trade.Id,
                trade.Id,
                trade.BuyOrderId,
                trade.SellOrderId,
                trade.StockId,
                trade.Quantity,
                trade.Price,
                trade.ExecutedAt);
            _dbContext.OutboxMessages.Add(new OutboxMessage(
                Guid.NewGuid(),
                submittedOrder.Id,
                OutboxMessage.TradingEngineOwner,
                OutboxMessage.TradeExecutedMessageType,
                JsonSerializer.Serialize(tradeExecuted, SerializerOptions)));
        }

        var latestTrade = trades[^1];
        var stockPriceUpdated = new StockPriceUpdatedEvent(
            latestTrade.Id,
            stockPriceUpdate.StockId,
            stockPriceUpdate.Symbol,
            stockPriceUpdate.Price);
        _dbContext.OutboxMessages.Add(new OutboxMessage(
            Guid.NewGuid(),
            submittedOrder.Id,
            OutboxMessage.TradingEngineOwner,
            OutboxMessage.StockPriceUpdatedMessageType,
            JsonSerializer.Serialize(stockPriceUpdated, SerializerOptions)));
    }

    private void LogCommittedTrades(IEnumerable<Trade> trades)
    {
        var accountIdsByOrderId = _dbContext.ChangeTracker
            .Entries<Order>()
            .Select(entry => entry.Entity)
            .ToDictionary(order => order.Id, order => order.AccountId);

        foreach (var trade in trades)
        {
            if (!accountIdsByOrderId.TryGetValue(trade.BuyOrderId, out var buyerAccountId) ||
                !accountIdsByOrderId.TryGetValue(trade.SellOrderId, out var sellerAccountId))
            {
                _logger.LogWarning(
                    "Trade {TradeId} committed without tracked account identifiers for orders {BuyOrderId} and {SellOrderId}.",
                    trade.Id,
                    trade.BuyOrderId,
                    trade.SellOrderId);
                continue;
            }

            _logger.LogInformation(
                "Orders {BuyOrderId} and {SellOrderId} matched for stock {StockId}, quantity {MatchedQuantity}, and price {MatchPrice}.",
                trade.BuyOrderId,
                trade.SellOrderId,
                trade.StockId,
                trade.Quantity,
                trade.Price);
            _logger.LogInformation(
                "Balance settlement completed for trade {TradeId} from buyer account {BuyerAccountId} to seller account {SellerAccountId} with settlement amount {SettlementAmount}.",
                trade.Id,
                buyerAccountId,
                sellerAccountId,
                trade.Quantity * trade.Price);
            _logger.LogInformation(
                "Portfolio settlement completed for trade {TradeId}, stock {StockId}, buyer account {BuyerAccountId}, seller account {SellerAccountId}, and quantity {Quantity}.",
                trade.Id,
                trade.StockId,
                buyerAccountId,
                sellerAccountId,
                trade.Quantity);
            _logger.LogInformation(
                "Trade {TradeId} executed for buy order {BuyOrderId}, sell order {SellOrderId}, stock {StockId}, quantity {Quantity}, and price {Price}.",
                trade.Id,
                trade.BuyOrderId,
                trade.SellOrderId,
                trade.StockId,
                trade.Quantity,
                trade.Price);
        }
    }
}
