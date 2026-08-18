namespace TradeCore.Api.DTOs.Accounts;

public sealed record AccountResponse(
    Guid Id,
    Guid UserId,
    string AccountNumber,
    decimal Balance);
