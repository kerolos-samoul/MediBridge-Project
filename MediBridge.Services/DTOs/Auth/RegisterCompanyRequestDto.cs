namespace MediBridge.Services.DTOs.Auth;

public sealed class RegisterCompanyRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public VerificationMetadataDto VerificationMetadata { get; set; } = new();
}
