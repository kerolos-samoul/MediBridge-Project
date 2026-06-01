namespace MediBridge.Services.Interfaces;

public sealed class AccountStatusDeniedException : Exception
{
    public AccountStatusDeniedException(string message)
        : base(message)
    {
    }
}
