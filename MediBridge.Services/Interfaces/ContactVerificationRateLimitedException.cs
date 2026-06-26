namespace MediBridge.Services.Interfaces;

public sealed class ContactVerificationRateLimitedException : Exception
{
    public ContactVerificationRateLimitedException(string message)
        : base(message)
    {
    }
}
