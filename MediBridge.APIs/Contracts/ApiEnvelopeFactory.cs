namespace MediBridge.APIs.Contracts;

public static class ApiEnvelopeFactory
{
    public static ApiEnvelope<T> Create<T>(int code, string message, T? data)
        => new(code, message, data);
}
