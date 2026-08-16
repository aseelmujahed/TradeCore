using TradeCore.Console.Models;

namespace TradeCore.Console.Services;

public class AccountService
{
    private readonly Dictionary<Guid, Account> _accounts = new();

    public Account CreateAccount(Guid userId, string accountNumber)
    {
        var account = new Account(Guid.NewGuid(), userId, accountNumber, 0m);

        _accounts.Add(account.Id, account);

        return account;
    }

    public void Deposit(Guid accountId, decimal amount)
    {
        var account = GetAccount(accountId);

        account.Deposit(amount);
    }

    public decimal GetBalance(Guid accountId)
    {
        var account = GetAccount(accountId);

        return account.Balance;
    }

    public Account GetAccount(Guid accountId)
    {
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            throw new KeyNotFoundException($"Account with ID '{accountId}' was not found.");
        }

        return account;
    }
}
