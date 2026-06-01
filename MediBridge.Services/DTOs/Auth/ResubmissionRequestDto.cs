namespace MediBridge.Services.DTOs.Auth;

public sealed class ResubmissionRequestDto
{
    public string ResubmissionToken { get; set; } = string.Empty;
    public string? Specialization { get; set; }
    public int? ExperienceYears { get; set; }
    public string? Location { get; set; }
    public string? CompanyName { get; set; }
    public string? LicenseNumber { get; set; }
    public string? ContactName { get; set; }
    public VerificationMetadataDto VerificationMetadata { get; set; } = new();
}
