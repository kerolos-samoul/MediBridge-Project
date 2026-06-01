namespace MediBridge.Services.Interfaces;

public sealed class RefreshReuseDetectedException : Exception
{
    public RefreshReuseDetectedException(string message)
        : base(message)
    {
    }
}
