namespace MediBridge.Services.DTOs.Auth;

public sealed class RegisterDoctorRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Specialization { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public string Location { get; set; } = string.Empty;
    public VerificationMetadataDto VerificationMetadata { get; set; } = new();
}
