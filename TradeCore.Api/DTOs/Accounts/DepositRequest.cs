using System.ComponentModel.DataAnnotations;

namespace TradeCore.Api.DTOs.Accounts;

public sealed class DepositRequest
{
    [Range(
        typeof(decimal),
        "0.0000000000000000000000000001",
        "79228162514264337593543950335",
        ErrorMessage = "Amount must be greater than 0.")]
    public decimal Amount { get; init; }
}
