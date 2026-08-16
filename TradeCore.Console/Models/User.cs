namespace TradeCore.Console.Models;

public class User
{
    public Guid Id { get; private set; }

    public string Username { get; private set; }

    public string Email { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public User(Guid id, string username, string email)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username cannot be empty.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        }

        Id = id;
        Username = username;
        Email = email;
        CreatedAt = DateTime.UtcNow;
    }
}
