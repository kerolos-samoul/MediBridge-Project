using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Entities.Profiles;

public sealed class DoctorProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string Specialization { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public string Location { get; set; } = string.Empty;
    public string VerificationDocumentType { get; set; } = string.Empty;
    public string VerificationOriginalFileName { get; set; } = string.Empty;
    public string VerificationContentType { get; set; } = string.Empty;
    public long VerificationSizeBytes { get; set; }
    public string VerificationReference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
