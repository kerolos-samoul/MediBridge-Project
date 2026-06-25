namespace MediBridge.Services.DTOs.Files;

public sealed record FileAccessDto(
    string FileId,
    string Url,
    DateTime ExpiresAtUtc);
