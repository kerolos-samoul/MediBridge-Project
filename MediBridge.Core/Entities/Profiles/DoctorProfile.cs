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
    public DateTime? SuspendedAtUtc { get; set; }
    public DateTime? SuspendedUntilUtc { get; set; }
    public DateTime? LastStatusChangedAtUtc { get; set; }
    public decimal? PricePerMessage { get; set; }
    public bool PricingIsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public void ApplyWarning(DateTime changedAtUtc)
    {
        EnsureUtc(changedAtUtc, nameof(changedAtUtc));
        Status = DoctorMarketplaceStatus.Warned;
        LastStatusChangedAtUtc = changedAtUtc;
        UpdatedAtUtc = changedAtUtc;
    }

    public void ReduceDailyLimit(int newDailyMessageLimit, DateTime changedAtUtc)
    {
        if (newDailyMessageLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newDailyMessageLimit), newDailyMessageLimit, "Daily message limit cannot be negative.");
        }

        EnsureUtc(changedAtUtc, nameof(changedAtUtc));
        DailyMessageLimit = newDailyMessageLimit;
        UpdatedAtUtc = changedAtUtc;
    }

    public void SuspendUntil(DateTime suspendedAtUtc, DateTime suspendedUntilUtc)
    {
        EnsureUtc(suspendedAtUtc, nameof(suspendedAtUtc));
        EnsureUtc(suspendedUntilUtc, nameof(suspendedUntilUtc));
        if (suspendedUntilUtc <= suspendedAtUtc)
        {
            throw new ArgumentException("Suspension expiry must be in the future relative to the suspension timestamp.", nameof(suspendedUntilUtc));
        }

        Status = DoctorMarketplaceStatus.Suspended;
        SuspendedAtUtc = suspendedAtUtc;
        SuspendedUntilUtc = suspendedUntilUtc;
        LastStatusChangedAtUtc = suspendedAtUtc;
        UpdatedAtUtc = suspendedAtUtc;
    }

    public void Reactivate(DateTime reactivatedAtUtc)
    {
        EnsureUtc(reactivatedAtUtc, nameof(reactivatedAtUtc));
        Status = DoctorMarketplaceStatus.Active;
        SuspendedAtUtc = null;
        SuspendedUntilUtc = null;
        LastStatusChangedAtUtc = reactivatedAtUtc;
        UpdatedAtUtc = reactivatedAtUtc;
    }

    public void ApplyActivityScore(decimal activityScore, DateTime calculatedAtUtc)
    {
        EnsureUtc(calculatedAtUtc, nameof(calculatedAtUtc));
        if (activityScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(activityScore), activityScore, "Activity score must be between 0.0 and 100.0.");
        }

        ActivityScore = Math.Round(activityScore, 1, MidpointRounding.AwayFromZero);
        UpdatedAtUtc = calculatedAtUtc;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Profile status timestamps must be UTC.", parameterName);
        }
    }
}
