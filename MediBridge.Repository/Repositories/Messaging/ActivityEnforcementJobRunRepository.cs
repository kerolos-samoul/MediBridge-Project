using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class ActivityEnforcementJobRunRepository : IActivityEnforcementJobRunRepository
{
    private readonly MediBridgeDbContext context;

    public ActivityEnforcementJobRunRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public Task<int> InterruptStaleRunningAsync(
        ActivityEnforcementJobType jobType,
        DateTime startedBeforeUtc,
        DateTime interruptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureUtc(startedBeforeUtc, nameof(startedBeforeUtc));
        EnsureUtc(interruptedAtUtc, nameof(interruptedAtUtc));
        return context.ActivityEnforcementJobRuns
            .Where(run => run.JobType == jobType
                && run.Status == ActivityEnforcementJobRunStatus.Running
                && run.StartedAtUtc < startedBeforeUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, ActivityEnforcementJobRunStatus.Interrupted)
                .SetProperty(run => run.CompletedAtUtc, interruptedAtUtc)
                .SetProperty(run => run.SafeFailureSummary, "Interrupted after a stale running record was recovered."),
                cancellationToken);
    }

    public async Task AddRunningAsync(ActivityEnforcementJobRun run, CancellationToken cancellationToken = default)
    {
        run.Validate();
        if (run.Status != ActivityEnforcementJobRunStatus.Running)
        {
            throw new ArgumentException("A newly added activity enforcement job run must be Running.", nameof(run));
        }

        await context.ActivityEnforcementJobRuns.AddAsync(run, cancellationToken);
    }

    public async Task<bool> CompleteAsync(
        string runId,
        ActivityEnforcementJobRunStatus terminalStatus,
        ActivityJobRunCounters counters,
        DateTime completedAtUtc,
        string? safeFailureSummary,
        CancellationToken cancellationToken = default)
    {
        if (terminalStatus == ActivityEnforcementJobRunStatus.Running)
        {
            throw new ArgumentException("A completion status must be terminal.", nameof(terminalStatus));
        }

        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        EnsureCounters(counters);
        var summary = SafeSummary.Normalize(safeFailureSummary, ActivityEnforcementJobRun.MaxSafeFailureSummaryLength);
        var rows = await context.ActivityEnforcementJobRuns
            .Where(run => run.Id == runId && run.Status == ActivityEnforcementJobRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, terminalStatus)
                .SetProperty(run => run.CompletedAtUtc, completedAtUtc)
                .SetProperty(run => run.ProcessedCount, counters.ProcessedCount)
                .SetProperty(run => run.SkippedCount, counters.SkippedCount)
                .SetProperty(run => run.CreatedCount, counters.CreatedCount)
                .SetProperty(run => run.UpdatedCount, counters.UpdatedCount)
                .SetProperty(run => run.FailedCount, counters.FailedCount)
                .SetProperty(run => run.SafeFailureSummary, summary),
                cancellationToken);
        return rows == 1;
    }

    public async Task<IReadOnlyList<ActivityEnforcementJobRun>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        return await context.ActivityEnforcementJobRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public Task<ActivityEnforcementJobRun?> FindExistingTargetRunAsync(
        ActivityEnforcementJobType jobType,
        DateOnly? targetScoreDateEgypt,
        DateOnly? targetWeekStartDateEgypt,
        CancellationToken cancellationToken = default)
    {
        return context.ActivityEnforcementJobRuns
            .AsNoTracking()
            .Where(run => run.JobType == jobType
                && run.TargetScoreDateEgypt == targetScoreDateEgypt
                && run.TargetWeekStartDateEgypt == targetWeekStartDateEgypt
                && (run.Status == ActivityEnforcementJobRunStatus.Succeeded
                    || run.Status == ActivityEnforcementJobRunStatus.PartiallySucceeded))
            .OrderByDescending(run => run.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void EnsureCounters(ActivityJobRunCounters counters)
    {
        if (new[] { counters.ProcessedCount, counters.SkippedCount, counters.CreatedCount, counters.UpdatedCount, counters.FailedCount }.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(counters), "Activity enforcement job counters cannot be negative.");
        }
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Operational timestamps must be UTC.", parameterName);
        }
    }
}
