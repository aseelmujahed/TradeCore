using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.Portfolio;
using TradeCore.Api.DTOs.Users;
using TradeCore.Console.Exceptions;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly AccountService _accountService;
    private readonly PortfolioService _portfolioService;

    public UsersController(
        UserService userService,
        AccountService accountService,
        PortfolioService portfolioService)
    {
        _userService = userService;
        _accountService = accountService;
        _portfolioService = portfolioService;
    }

    [HttpPost]
    public ActionResult<UserResponse> CreateUser(CreateUserRequest request)
    {
        try
        {
            var user = _userService.CreateUser(request.Username.Trim(), request.Email);

            return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, ToResponse(user));
        }
        catch (DuplicateUserEmailException)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<UserResponse>> GetUsers()
    {
        return Ok(_userService.GetAllUsers().Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<UserResponse> GetUserById(Guid id)
    {
        var user = _userService.GetUser(id);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(user));
    }

    [HttpGet("{id:guid}/portfolio")]
    public ActionResult<IReadOnlyList<PortfolioPositionResponse>> GetPortfolio(Guid id)
    {
        if (_userService.GetUser(id) is null)
        {
            return NotFound();
        }

        var account = _accountService.GetAccountByUserId(id);

        if (account is null)
        {
            return NotFound($"Account for user '{id}' was not found.");
        }

        return Ok(_portfolioService.GetPortfolioPositions(account.Id)
            .Select(ToResponse)
            .ToList());
    }

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse(user.Id, user.Username, user.Email, user.CreatedAt);
    }

    private static PortfolioPositionResponse ToResponse(PortfolioPosition position)
    {
        return new PortfolioPositionResponse(
            position.AccountId,
            position.StockId,
            position.Quantity,
            position.AveragePrice);
    }
}
