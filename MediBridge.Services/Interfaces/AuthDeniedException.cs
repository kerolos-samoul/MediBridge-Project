namespace MediBridge.Services.Interfaces;

public sealed class AuthDeniedException : Exception
{
    public AuthDeniedException(string message)
        : base(message)
    {
    }
}
