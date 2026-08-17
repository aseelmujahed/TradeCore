using System.ComponentModel.DataAnnotations;

namespace TradeCore.Api.DTOs.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public NotEmptyGuidAttribute()
        : base("AccountId must not be empty.")
    {
    }

    public override bool IsValid(object? value)
    {
        return value is Guid id && id != Guid.Empty;
    }
}
