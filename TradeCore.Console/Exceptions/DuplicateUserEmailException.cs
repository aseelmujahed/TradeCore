namespace TradeCore.Console.Exceptions;

public sealed class DuplicateUserEmailException : InvalidOperationException
{
    public DuplicateUserEmailException()
        : base("A user with this email already exists.")
    {
    }
}
