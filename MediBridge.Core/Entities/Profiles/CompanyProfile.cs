using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Entities.Profiles;

public sealed class CompanyProfile : ISoftDeleteRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string VerificationDocumentType { get; set; } = string.Empty;
    public string VerificationOriginalFileName { get; set; } = string.Empty;
    public string VerificationContentType { get; set; } = string.Empty;
    public long VerificationSizeBytes { get; set; }
    public string VerificationReference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
