namespace MediBridge.Services.Interfaces;

public sealed class RegistrationConflictException : Exception
{
    public RegistrationConflictException(string message)
        : base(message)
    {
    }
}
