using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Messaging;

public sealed class ActivityEnforcementJobRun
{
    public const int MaxSafeFailureSummaryLength = 2000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ActivityEnforcementJobType JobType { get; set; }
    public DateOnly? TargetScoreDateEgypt { get; set; }
    public DateOnly? TargetWeekStartDateEgypt { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public ActivityEnforcementJobRunStatus Status { get; set; } = ActivityEnforcementJobRunStatus.Running;
    public int ProcessedCount { get; set; }
    public int SkippedCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int FailedCount { get; set; }
    public string? SafeFailureSummary { get; set; }
    public string? RequestedByAdminUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public void Validate()
    {
        if (StartedAtUtc.Kind != DateTimeKind.Utc
            || CreatedAtUtc.Kind != DateTimeKind.Utc
            || (CompletedAtUtc.HasValue && CompletedAtUtc.Value.Kind != DateTimeKind.Utc))
        {
            throw new InvalidOperationException("Activity enforcement job timestamps must be UTC.");
        }

        if (JobType == ActivityEnforcementJobType.DailyActivityScore && TargetScoreDateEgypt is null)
        {
            throw new InvalidOperationException("Daily activity score runs require a target score date.");
        }

        if (JobType == ActivityEnforcementJobType.WeeklyEnforcement && TargetWeekStartDateEgypt is null)
        {
            throw new InvalidOperationException("Weekly enforcement runs require a target week start.");
        }

        if (new[] { ProcessedCount, SkippedCount, CreatedCount, UpdatedCount, FailedCount }.Any(value => value < 0))
        {
            throw new InvalidOperationException("Activity enforcement job counters cannot be negative.");
        }

        if (SafeFailureSummary?.Length > MaxSafeFailureSummaryLength)
        {
            throw new InvalidOperationException($"Safe failure summaries cannot exceed {MaxSafeFailureSummaryLength} characters.");
        }
    }
}
