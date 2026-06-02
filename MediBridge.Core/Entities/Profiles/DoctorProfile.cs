using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Profiles;

public sealed class DoctorProfile : ISoftDeleteRecord
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
    public int DailyMessageLimit { get; set; }
    public int MinimumWeeklyRequirement { get; set; }
    public int? RequestedDailyMessageLimit { get; set; }
    public int? RequestedMinimumWeeklyRequirement { get; set; }
    public decimal ActivityScore { get; set; } = 95m;
    public DoctorMarketplaceStatus Status { get; set; } = DoctorMarketplaceStatus.Active;
    public decimal? PricePerMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
