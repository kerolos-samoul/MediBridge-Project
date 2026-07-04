using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Messaging;

public sealed class DeliveryRecoveryDispatch : IConcurrencyTrackedRecord
{
    public const int MaxSafeFailureSummaryLength = 2000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateOnly BusinessDateEgypt { get; set; }
    public DeliveryJobType JobType { get; set; }
    public RecoveryDispatchStatus Status { get; set; } = RecoveryDispatchStatus.Pending;
    public string? SchedulerJobId { get; set; }
    public string? DependsOnDispatchId { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
    public DateTime? EnqueuedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? SafeFailureSummary { get; set; }
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("A recovery dispatch id is required.");
        }

        EnsureUtc(ClaimedAtUtc, nameof(ClaimedAtUtc));
        if (EnqueuedAtUtc is { } enqueuedAtUtc)
        {
            EnsureUtc(enqueuedAtUtc, nameof(EnqueuedAtUtc));
        }

        if (CompletedAtUtc is { } completedAtUtc)
        {
            EnsureUtc(completedAtUtc, nameof(CompletedAtUtc));
        }

        if (SchedulerJobId?.Length > 100 || DependsOnDispatchId?.Length > 64)
        {
            throw new InvalidOperationException("Recovery scheduler identifiers exceed their safe persistence bounds.");
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
