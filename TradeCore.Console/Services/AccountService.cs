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

    public Account CreateAccount(Guid userId, string accountNumber)
    {
        if (!_dbContext.Users.Any(user => user.Id == userId))
        {
            throw new KeyNotFoundException($"User with ID '{userId}' was not found.");
        }

        var account = new Account(Guid.NewGuid(), userId, accountNumber, 0m);

        _dbContext.Accounts.Add(account);
        _dbContext.SaveChanges();

        return account;
    }

    public void Deposit(Guid accountId, decimal amount)
    {
        var account = GetAccount(accountId);

        account.Deposit(amount);
        _dbContext.SaveChanges();
    }

    public decimal GetBalance(Guid accountId)
    {
        var account = GetAccount(accountId);

        return account.Balance;
    }

    public Account? GetAccountByUserId(Guid userId)
    {
        return _dbContext.Accounts.SingleOrDefault(account => account.UserId == userId);
    }

    public Account GetAccount(Guid accountId)
    {
        var account = _dbContext.Accounts.SingleOrDefault(account => account.Id == accountId);

        if (account is null)
        {
            throw new KeyNotFoundException($"Account with ID '{accountId}' was not found.");
        }

        return account;
    }
}
