using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Messaging;

public sealed class DeliveryJobRun : IConcurrencyTrackedRecord
{
    public const int MaxSafeFailureSummaryLength = 2000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DeliveryJobType JobType { get; set; }
    public DateOnly BusinessDateEgypt { get; set; }
    public DeliveryJobRunStatus Status { get; set; } = DeliveryJobRunStatus.Running;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ExaminedCount { get; set; }
    public int ActivatedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int CancelledCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public string? SafeFailureSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("A job run id is required.");
        }

        EnsureUtc(StartedAtUtc, nameof(StartedAtUtc));
        EnsureUtc(CreatedAtUtc, nameof(CreatedAtUtc));
        if (CompletedAtUtc is { } completedAtUtc)
        {
            EnsureUtc(completedAtUtc, nameof(CompletedAtUtc));
            if (completedAtUtc < StartedAtUtc)
            {
                throw new InvalidOperationException("A job run cannot complete before it starts.");
            }
        }

        if (Status == DeliveryJobRunStatus.Running && CompletedAtUtc is not null
            || Status != DeliveryJobRunStatus.Running && CompletedAtUtc is null)
        {
            throw new InvalidOperationException("Running jobs must be incomplete and terminal jobs must have a completion time.");
        }

        if (new[] { ExaminedCount, ActivatedCount, ExpiredCount, CancelledCount, SkippedCount, FailedCount }.Any(value => value < 0))
        {
            throw new InvalidOperationException("Job run counters cannot be negative.");
        }

        if (SafeFailureSummary?.Length > MaxSafeFailureSummaryLength)
        {
            throw new InvalidOperationException($"Safe failure summaries cannot exceed {MaxSafeFailureSummaryLength} characters.");
        }
    }

    private static void EnsureUtc(DateTime value, string propertyName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException($"{propertyName} must be UTC.");
        }
    }
}
