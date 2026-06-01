namespace MediBridge.Services.DTOs.Auth;

public sealed class VerificationMetadataDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Reference { get; set; } = string.Empty;
}
