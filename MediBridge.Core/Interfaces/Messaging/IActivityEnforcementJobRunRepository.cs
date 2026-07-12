using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IActivityEnforcementJobRunRepository
{
    Task<int> InterruptStaleRunningAsync(ActivityEnforcementJobType jobType, DateTime startedBeforeUtc, DateTime interruptedAtUtc, CancellationToken cancellationToken = default);
    Task AddRunningAsync(ActivityEnforcementJobRun run, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(string runId, ActivityEnforcementJobRunStatus terminalStatus, ActivityJobRunCounters counters, DateTime completedAtUtc, string? safeFailureSummary, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityEnforcementJobRun>> ListRecentAsync(int take, CancellationToken cancellationToken = default);
    Task<ActivityEnforcementJobRun?> FindExistingTargetRunAsync(ActivityEnforcementJobType jobType, DateOnly? targetScoreDateEgypt, DateOnly? targetWeekStartDateEgypt, CancellationToken cancellationToken = default);
}
