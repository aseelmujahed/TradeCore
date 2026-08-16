using Microsoft.AspNetCore.Mvc;
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
    public ActionResult<User> CreateUser(CreateUserRequest request)
    {
        var user = _userService.CreateUser(request.Username, request.Email);

        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<User>> GetUsers()
    {
        return Ok(_userService.GetAllUsers());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<User> GetUserById(Guid id)
    {
        var user = _userService.GetUser(id);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    [HttpGet("{id:guid}/portfolio")]
    public ActionResult<IReadOnlyList<PortfolioPosition>> GetPortfolio(Guid id)
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

        return Ok(_portfolioService.GetPortfolioPositions(account.Id));
    }
}

public record CreateUserRequest(string Username, string Email);
