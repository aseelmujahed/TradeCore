namespace TradeCore.Console.Models;

public class Account
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string AccountNumber { get; private set; }

    public decimal Balance { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public Account(Guid id, Guid userId, string accountNumber, decimal balance)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Account ID cannot be empty.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new ArgumentException("Account number cannot be empty.", nameof(accountNumber));
        }

        if (balance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(balance), "Balance cannot be negative.");
        }

        Id = id;
        UserId = userId;
        AccountNumber = accountNumber;
        Balance = balance;
        CreatedAt = DateTime.UtcNow;
    }

    public void Deposit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Deposit amount must be greater than zero.");
        }

        Balance += amount;
    }

    public void Debit(decimal amount)
    {
        EnsureCanDebit(amount);

        Balance -= amount;
    }

    public void EnsureCanDebit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be greater than zero.");
        }

        if (amount > Balance)
        {
            throw new InvalidOperationException("Insufficient account balance for this trade.");
        }
    }
}
