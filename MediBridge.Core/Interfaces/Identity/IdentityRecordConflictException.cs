namespace MediBridge.Core.Interfaces.Identity;

public sealed class IdentityRecordConflictException : Exception
{
    public IdentityRecordConflictException(string message)
        : base(message)
    {
    }

    public IdentityRecordConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
