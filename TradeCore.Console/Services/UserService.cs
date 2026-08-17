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

    public async Task<User> CreateUserAsync(string username, string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (await _dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken))
        {
            throw new DuplicateUserEmailException();
        }

        var user = new User(Guid.NewGuid(), username, normalizedEmail);
        var account = new Account(
            Guid.NewGuid(),
            user.Id,
            $"ACC-{user.Id:N}",
            0m);

        _dbContext.Users.Add(user);
        _dbContext.Accounts.Add(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return user;
    }

    public async Task<IReadOnlyList<User>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users.ToListAsync(cancellationToken);
    }

    public Task<User?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
    }
}
