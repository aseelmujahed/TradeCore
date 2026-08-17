using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Data;
using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class AccountService
{
    private readonly TradeCoreDbContext _dbContext;

    public AccountService(TradeCoreDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Account> CreateAccountAsync(Guid userId, string accountNumber, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            throw new KeyNotFoundException($"User with ID '{userId}' was not found.");
        }

        var account = new Account(Guid.NewGuid(), userId, accountNumber, 0m);

        _dbContext.Accounts.Add(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return account;
    }

    public async Task DepositAsync(Guid accountId, decimal amount, CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(accountId, cancellationToken);

        account.Deposit(amount);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<decimal> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(accountId, cancellationToken);

        return account.Balance;
    }

    public Task<Account?> GetAccountByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Accounts.SingleOrDefaultAsync(account => account.UserId == userId, cancellationToken);
    }

    public async Task<Account> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Accounts.SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);

        if (account is null)
        {
            throw new KeyNotFoundException($"Account with ID '{accountId}' was not found.");
        }

        return account;
    }
}
