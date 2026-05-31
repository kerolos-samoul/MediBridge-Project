namespace MediBridge.APIs.Contracts;

public sealed record ApiEnvelope<T>(int Code, string Message, T? Data);
