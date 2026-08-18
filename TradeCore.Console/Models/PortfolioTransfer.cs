using TradeCore.Console.Enums;

namespace TradeCore.Console.Models;

public class PortfolioTransfer
{
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid StockId { get; private set; }
    public int Quantity { get; private set; }
    public decimal AveragePrice { get; private set; }
    public PortfolioTransferStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public PortfolioTransfer(Guid id, Guid accountId, Guid stockId, int quantity, decimal averagePrice)
    {
        if (id == Guid.Empty) throw new ArgumentException("Portfolio transfer ID cannot be empty.", nameof(id));
        if (accountId == Guid.Empty) throw new ArgumentException("Account ID cannot be empty.", nameof(accountId));
        if (stockId == Guid.Empty) throw new ArgumentException("Stock ID cannot be empty.", nameof(stockId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        if (averagePrice <= 0) throw new ArgumentOutOfRangeException(nameof(averagePrice), "Average price must be greater than zero.");

        Id = id;
        AccountId = accountId;
        StockId = stockId;
        Quantity = quantity;
        AveragePrice = averagePrice;
        Status = PortfolioTransferStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public void Complete(DateTime completedAt)
    {
        if (Status != PortfolioTransferStatus.Pending)
            throw new InvalidOperationException("Only pending portfolio transfers can be completed.");

        Status = PortfolioTransferStatus.Completed;
        CompletedAt = completedAt;
    }

    public void Reject()
    {
        if (Status != PortfolioTransferStatus.Pending)
            throw new InvalidOperationException("Only pending portfolio transfers can be rejected.");

        Status = PortfolioTransferStatus.Rejected;
    }
}
