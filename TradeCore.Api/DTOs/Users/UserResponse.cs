namespace TradeCore.Api.DTOs.Users;

public record UserResponse(Guid Id, string Username, string Email, DateTime CreatedAt);
