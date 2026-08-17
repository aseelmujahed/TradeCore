using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Exceptions;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class UserService
{
    private readonly TradeCoreDbContext _dbContext;

    public UserService(TradeCoreDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public User CreateUser(string username, string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (_dbContext.Users.Any(user => user.Email == normalizedEmail))
        {
            throw new DuplicateUserEmailException();
        }

        var user = new User(Guid.NewGuid(), username, normalizedEmail);

        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();

        return user;
    }

    public IReadOnlyList<User> GetAllUsers()
    {
        return _dbContext.Users.ToList();
    }

    public User? GetUser(Guid id)
    {
        return _dbContext.Users.SingleOrDefault(user => user.Id == id);
    }
}
