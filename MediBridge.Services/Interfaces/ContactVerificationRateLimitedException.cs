namespace MediBridge.Services.Interfaces;

public sealed class ContactVerificationRateLimitedException : Exception
{
    public ContactVerificationRateLimitedException(string message = "Too many verification attempts.") : base(message)
    {
    }
}
