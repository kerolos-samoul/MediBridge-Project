using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Profiles;

public sealed class WeeklyEnforcementDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public DateOnly WeekStartDateEgypt { get; set; }
    public DateOnly WeekEndDateEgypt { get; set; }
    public int MinimumWeeklyRequirement { get; set; }
    public int InteractionCount { get; set; }
    public WeeklyEnforcementDecisionType Decision { get; set; }
    public bool SuspensionOverlapped { get; set; }
    public int RollingViolationCountAfterDecision { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? JobRunId { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DoctorId))
        {
            throw new InvalidOperationException("Weekly enforcement decision requires a doctor id.");
        }

        if (WeekStartDateEgypt.DayOfWeek != DayOfWeek.Monday)
        {
            throw new InvalidOperationException("Weekly enforcement week start must be Monday.");
        }

        if (WeekEndDateEgypt != WeekStartDateEgypt.AddDays(7))
        {
            throw new InvalidOperationException("Weekly enforcement week end must be the following Monday boundary.");
        }

        if (MinimumWeeklyRequirement < 0 || InteractionCount < 0 || RollingViolationCountAfterDecision < 0)
        {
            throw new InvalidOperationException("Weekly enforcement counts cannot be negative.");
        }

        if (CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Weekly enforcement timestamps must be UTC.");
        }

        if (Decision == WeeklyEnforcementDecisionType.SuspensionSkipped && !SuspensionOverlapped)
        {
            throw new InvalidOperationException("Suspension-skipped decisions require suspension overlap evidence.");
        }

        if (Decision == WeeklyEnforcementDecisionType.Violation
            && (SuspensionOverlapped || InteractionCount >= MinimumWeeklyRequirement))
        {
            throw new InvalidOperationException("Violation decisions require no suspension overlap and below-threshold interactions.");
        }

        if (Decision == WeeklyEnforcementDecisionType.Compliant
            && (SuspensionOverlapped || InteractionCount < MinimumWeeklyRequirement))
        {
            throw new InvalidOperationException("Compliant decisions require no suspension overlap and enough interactions.");
        }
    }
}
