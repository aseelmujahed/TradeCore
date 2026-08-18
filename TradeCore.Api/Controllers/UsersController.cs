using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.Accounts;
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
    public async Task<ActionResult<UserResponse>> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _userService.CreateUserAsync(request.Username.Trim(), request.Email, cancellationToken);

            return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, ToResponse(user));
        }
        catch (DuplicateUserEmailException)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _userService.GetAllUsersAsync(cancellationToken);
        return Ok(users.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetUserById(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetUserAsync(id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(user));
    }

    [HttpGet("{id:guid}/portfolio")]
    public async Task<ActionResult<IReadOnlyList<PortfolioPositionResponse>>> GetPortfolio(Guid id, CancellationToken cancellationToken)
    {
        if (await _userService.GetUserAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var account = await _accountService.GetAccountByUserIdAsync(id, cancellationToken);

        if (account is null)
        {
            return NotFound($"Account for user '{id}' was not found.");
        }

        var positions = await _portfolioService.GetPortfolioPositionsAsync(account.Id, cancellationToken);
        return Ok(positions.Select(ToResponse).ToList());
    }

    [HttpGet("{userId:guid}/account")]
    public async Task<ActionResult<AccountResponse>> GetAccount(Guid userId, CancellationToken cancellationToken)
    {
        if (await _userService.GetUserAsync(userId, cancellationToken) is null)
        {
            return NotFound();
        }

        var account = await _accountService.GetAccountByUserIdAsync(userId, cancellationToken);

        if (account is null)
        {
            return NotFound($"Account for user '{userId}' was not found.");
        }

        return Ok(ToResponse(account));
    }

    [HttpPost("{userId:guid}/account/deposit")]
    public async Task<ActionResult<AccountResponse>> Deposit(
        Guid userId,
        DepositRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
        {
            return BadRequest("Amount must be greater than 0.");
        }

        if (await _userService.GetUserAsync(userId, cancellationToken) is null)
        {
            return NotFound();
        }

        var account = await _accountService.GetAccountByUserIdAsync(userId, cancellationToken);

        if (account is null)
        {
            return NotFound($"Account for user '{userId}' was not found.");
        }

        await _accountService.DepositAsync(account.Id, request.Amount, cancellationToken);
        var updatedAccount = await _accountService.GetAccountAsync(account.Id, cancellationToken);

        return Ok(ToResponse(updatedAccount));
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

    private static AccountResponse ToResponse(Account account)
    {
        return new AccountResponse(account.Id, account.UserId, account.AccountNumber, account.Balance);
    }
}
