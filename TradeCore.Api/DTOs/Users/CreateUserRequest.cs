using System.ComponentModel.DataAnnotations;

namespace TradeCore.Api.DTOs.Users;

public record CreateUserRequest(
    [param: Required(ErrorMessage = "Username is required.")]
    [param: StringLength(50, ErrorMessage = "Username must be 50 characters or fewer.")]
    [param: RegularExpression(@".*\S.*", ErrorMessage = "Username cannot be whitespace only.")]
    string Username,

    [param: Required(ErrorMessage = "Email is required.")]
    [param: EmailAddress(ErrorMessage = "Email is not valid.")]
    [param: StringLength(254, ErrorMessage = "Email must be 254 characters or fewer.")]
    string Email);
