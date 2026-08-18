using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public sealed class PortfolioTransferService(
    TradeCoreDbContext dbContext,
    AccountService accountService,
    StockService stockService,
    PortfolioService portfolioService)
{
    public async Task<PortfolioTransfer> RequestTransferAsync(Guid accountId, string stockSymbol, int quantity, decimal averagePrice, CancellationToken cancellationToken = default)
    {
        await accountService.GetAccountAsync(accountId, cancellationToken);
        var stock = await stockService.GetStockBySymbolAsync(stockSymbol, cancellationToken);
        var transfer = new PortfolioTransfer(Guid.NewGuid(), accountId, stock.Id, quantity, averagePrice);
        dbContext.PortfolioTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return transfer;
    }

    public async Task<PortfolioTransfer> GetTransferAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.PortfolioTransfers.SingleOrDefaultAsync(transfer => transfer.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Portfolio transfer with ID '{id}' was not found.");
    }

    public async Task<IReadOnlyList<PortfolioTransfer>> GetTransfersAsync(CancellationToken cancellationToken = default) =>
        await dbContext.PortfolioTransfers.OrderByDescending(transfer => transfer.CreatedAt).ToListAsync(cancellationToken);

    public async Task<PortfolioTransfer> CompleteTransferAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var transfer = await GetTransferAsync(id, cancellationToken);

        transfer.Complete(DateTime.UtcNow);
        // This is the simulated/manual external verification step until authorization is introduced.
        await portfolioService.AddExternalSharesAsync(transfer.AccountId, transfer.StockId, transfer.Quantity, transfer.AveragePrice, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return transfer;
    }

    public async Task<PortfolioTransfer> RejectTransferAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transfer = await GetTransferAsync(id, cancellationToken);
        transfer.Reject();
        await dbContext.SaveChangesAsync(cancellationToken);
        return transfer;
    }
}
