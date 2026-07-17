namespace MediBridge.Core.Entities.Policies;

public sealed class DoctorPriceHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public decimal? PreviousPricePerMessage { get; set; }
    public decimal? NewPricePerMessage { get; set; }
    public bool PricingIsActive { get; set; } = true;
    public string ChangedByAdminUserId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CorrectsHistoryId { get; set; }
}

public sealed class PlatformFeePolicyHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public decimal FeePercent { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string ChangedByAdminUserId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CorrectsHistoryId { get; set; }
}

public sealed class ActivityScoreHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public decimal ActivityScore { get; set; }
    public decimal? ResponseSpeedScore { get; set; }
    public decimal? EngagementScore { get; set; }
    public decimal? FeedbackScore { get; set; }
    public DateOnly WindowStartDateEgypt { get; set; }
    public DateOnly WindowEndDateEgypt { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CorrectsHistoryId { get; set; }
}
