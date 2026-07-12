namespace MediBridge.Core.Entities.Profiles;

public sealed class DoctorWeeklyViolation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public string WeeklyEnforcementDecisionId { get; set; } = string.Empty;
    public DateOnly WeekStartDateEgypt { get; set; }
    public DateOnly WeekEndDateEgypt { get; set; }
    public int MinimumWeeklyRequirement { get; set; }
    public int InteractionCount { get; set; }
    public int RollingViolationCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? AuditEventId { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DoctorId) || string.IsNullOrWhiteSpace(WeeklyEnforcementDecisionId))
        {
            throw new InvalidOperationException("Weekly violations require doctor and decision ids.");
        }

        if (WeekStartDateEgypt.DayOfWeek != DayOfWeek.Monday || WeekEndDateEgypt != WeekStartDateEgypt.AddDays(7))
        {
            throw new InvalidOperationException("Weekly violation boundaries must be a Monday-to-Monday Cairo week.");
        }

        if (MinimumWeeklyRequirement < 0 || InteractionCount < 0 || RollingViolationCount < 0)
        {
            throw new InvalidOperationException("Weekly violation counts cannot be negative.");
        }

        if (InteractionCount >= MinimumWeeklyRequirement)
        {
            throw new InvalidOperationException("Weekly violations require interactions below the minimum requirement.");
        }

        if (CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Weekly violation timestamps must be UTC.");
        }
    }
}
