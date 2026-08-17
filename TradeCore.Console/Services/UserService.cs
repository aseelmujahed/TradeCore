using TradeCore.Console.Exceptions;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class UserService
{
    private readonly Dictionary<Guid, User> _users = new();

    public User CreateUser(string username, string email)
    {
        var normalizedEmail = email.Trim();

        if (_users.Values.Any(user => string.Equals(
                user.Email,
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateUserEmailException();
        }

        var user = new User(Guid.NewGuid(), username, normalizedEmail);

        _users.Add(user.Id, user);

        return user;
    }

    public IReadOnlyList<User> GetAllUsers()
    {
        return _users.Values.ToList();
    }

    public User? GetUser(Guid id)
    {
        _users.TryGetValue(id, out var user);

        return user;
    }
}
