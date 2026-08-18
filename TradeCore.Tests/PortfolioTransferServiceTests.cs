using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Enums;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Tests;

public sealed class PortfolioTransferServiceTests
{
    [Fact]
    public async Task RequestTransfer_CreatesPendingTransfer_WithoutChangingPortfolio()
    {
        using var database = new TradingTestDatabase();
        await using var context = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(context, sellerShares: 0);
        var service = CreateService(context);

        var transfer = await service.RequestTransferAsync(scenario.Buyer.Id, scenario.Stock.Symbol, 10, 350m);

        Assert.Equal(PortfolioTransferStatus.Pending, transfer.Status);
        Assert.Null(transfer.CompletedAt);
        Assert.Equal(0, await context.PortfolioPositions.CountAsync(position => position.AccountId == scenario.Buyer.Id));
    }

    [Theory]
    [InlineData(0, 350)]
    [InlineData(10, 0)]
    public async Task RequestTransfer_RejectsInvalidValues(int quantity, decimal averagePrice)
    {
        using var database = new TradingTestDatabase();
        await using var context = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(context, sellerShares: 0);

        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(() =>
            CreateService(context).RequestTransferAsync(scenario.Buyer.Id, scenario.Stock.Symbol, quantity, averagePrice));
    }

    [Fact]
    public async Task RequestTransfer_RejectsUnknownAccountAndStock()
    {
        using var database = new TradingTestDatabase();
        await using var context = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(context, sellerShares: 0);
        var service = CreateService(context);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RequestTransferAsync(Guid.NewGuid(), scenario.Stock.Symbol, 1, 1m));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RequestTransferAsync(scenario.Buyer.Id, "NOPE", 1, 1m));
    }

    [Fact]
    public async Task CompleteTransfer_AddsPosition_PersistsAndSetsCompletionTime()
    {
        using var database = new TradingTestDatabase();
        Guid transferId;
        var scenario = default(TradingScenario)!;
        await using (var context = database.CreateContext())
        {
            scenario = await database.SeedScenarioAsync(context, sellerShares: 0);
            var service = CreateService(context);
            transferId = (await service.RequestTransferAsync(scenario.Buyer.Id, scenario.Stock.Symbol, 10, 350m)).Id;
            var completed = await service.CompleteTransferAsync(transferId);
            Assert.Equal(PortfolioTransferStatus.Completed, completed.Status);
            Assert.NotNull(completed.CompletedAt);
        }
        await using var freshContext = database.CreateContext();
        var position = await freshContext.PortfolioPositions.SingleAsync(position => position.AccountId == scenario.Buyer.Id);
        Assert.Equal(10, position.Quantity);
        Assert.Equal(350m, position.AveragePrice);
        Assert.Equal(PortfolioTransferStatus.Completed, (await freshContext.PortfolioTransfers.SingleAsync(transfer => transfer.Id == transferId)).Status);
    }

    [Fact]
    public async Task CompleteTransfer_UsesExistingWeightedAverage_AndCannotBeRepeated()
    {
        using var database = new TradingTestDatabase();
        await using var context = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(context, sellerShares: 0, buyerShares: 10);
        var service = CreateService(context);
        var transfer = await service.RequestTransferAsync(scenario.Buyer.Id, scenario.Stock.Symbol, 10, 60m);

        await service.CompleteTransferAsync(transfer.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteTransferAsync(transfer.Id));

        var position = await context.PortfolioPositions.SingleAsync(position => position.AccountId == scenario.Buyer.Id);
        Assert.Equal(20, position.Quantity);
        Assert.Equal(50m, position.AveragePrice);
    }

    [Fact]
    public async Task RejectedTransfer_CannotComplete_AndNeverChangesPortfolio()
    {
        using var database = new TradingTestDatabase();
        await using var context = database.CreateContext();
        var scenario = await database.SeedScenarioAsync(context, sellerShares: 0);
        var service = CreateService(context);
        var transfer = await service.RequestTransferAsync(scenario.Buyer.Id, scenario.Stock.Symbol, 10, 350m);

        await service.RejectTransferAsync(transfer.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteTransferAsync(transfer.Id));

        Assert.Equal(0, await context.PortfolioPositions.CountAsync(position => position.AccountId == scenario.Buyer.Id));
    }

    private static PortfolioTransferService CreateService(TradeCore.Console.Data.TradeCoreDbContext context)
    {
        var accounts = new AccountService(context);
        var stocks = new StockService(context);
        return new PortfolioTransferService(context, accounts, stocks, new PortfolioService(context, accounts, stocks));
    }
}
