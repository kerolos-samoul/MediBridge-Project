using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public sealed record DeliveryJobRunCounters(
    int ExaminedCount,
    int ActivatedCount,
    int ExpiredCount,
    int CancelledCount,
    int SkippedCount,
    int FailedCount);

public interface IDeliveryJobRunRepository
{
    Task<int> InterruptStaleRunningAsync(DeliveryJobType jobType, DateTime startedBeforeUtc, DateTime interruptedAtUtc, CancellationToken cancellationToken = default);
    Task AddRunningAsync(DeliveryJobRun run, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(string runId, DeliveryJobRunStatus terminalStatus, DeliveryJobRunCounters counters, DateTime completedAtUtc, string? safeFailureSummary, CancellationToken cancellationToken = default);
    Task<bool> HasCurrentDateCoverageAsync(DeliveryJobType jobType, DateOnly businessDateEgypt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryJobRun>> ListRecentAsync(int take, CancellationToken cancellationToken = default);
}
