using System.ComponentModel.DataAnnotations;
using TradeCore.Api.DTOs.Validation;

namespace TradeCore.Api.DTOs.PortfolioTransfers;

public sealed class CreatePortfolioTransferRequest
{
    [NotEmptyGuid]
    public Guid AccountId { get; init; }

    [Required]
    public string StockSymbol { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [Range(typeof(decimal), "0.0001", "79228162514264337593543950335")]
    public decimal AveragePrice { get; init; }
}
