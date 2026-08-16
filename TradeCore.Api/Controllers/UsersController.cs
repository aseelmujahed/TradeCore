using Microsoft.AspNetCore.Mvc;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
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
}

public record CreateUserRequest(string Username, string Email);
