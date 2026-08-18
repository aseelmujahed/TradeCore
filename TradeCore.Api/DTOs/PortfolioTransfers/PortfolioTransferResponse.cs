using TradeCore.Console.Enums;

namespace TradeCore.Api.DTOs.PortfolioTransfers;

public sealed record PortfolioTransferResponse(Guid Id, Guid AccountId, Guid StockId, string StockSymbol, int Quantity, decimal AveragePrice, PortfolioTransferStatus Status, DateTime CreatedAt, DateTime? CompletedAt);
