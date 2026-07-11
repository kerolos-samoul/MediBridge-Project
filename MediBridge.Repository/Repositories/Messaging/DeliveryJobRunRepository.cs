using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class DeliveryJobRunRepository : IDeliveryJobRunRepository
{
    private readonly MediBridgeDbContext context;

    public DeliveryJobRunRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public Task<int> InterruptStaleRunningAsync(
        DeliveryJobType jobType,
        DateTime startedBeforeUtc,
        DateTime interruptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureUtc(startedBeforeUtc, nameof(startedBeforeUtc));
        EnsureUtc(interruptedAtUtc, nameof(interruptedAtUtc));
        return context.DeliveryJobRuns
            .Where(run => run.JobType == jobType
                && run.Status == DeliveryJobRunStatus.Running
                && run.StartedAtUtc < startedBeforeUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, DeliveryJobRunStatus.Interrupted)
                .SetProperty(run => run.CompletedAtUtc, interruptedAtUtc)
                .SetProperty(run => run.UpdatedAtUtc, interruptedAtUtc)
                .SetProperty(run => run.SafeFailureSummary, "Interrupted after a stale running record was recovered."),
                cancellationToken);
    }

    public async Task AddRunningAsync(DeliveryJobRun run, CancellationToken cancellationToken = default)
    {
        run.Validate();
        if (run.Status != DeliveryJobRunStatus.Running)
        {
            throw new ArgumentException("A newly added job run must be Running.", nameof(run));
        }

        await context.DeliveryJobRuns.AddAsync(run, cancellationToken);
    }

    public Task<bool> CompleteAsync(
        string runId,
        DeliveryJobRunStatus terminalStatus,
        DeliveryJobRunCounters counters,
        DateTime completedAtUtc,
        string? safeFailureSummary,
        CancellationToken cancellationToken = default)
    {
        if (terminalStatus == DeliveryJobRunStatus.Running)
        {
            throw new ArgumentException("A completion status must be terminal.", nameof(terminalStatus));
        }

        EnsureCounters(counters);
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        var summary = SafeSummary.Normalize(safeFailureSummary, DeliveryJobRun.MaxSafeFailureSummaryLength);
        return CompleteCoreAsync();

        async Task<bool> CompleteCoreAsync()
        {
            var rows = await context.DeliveryJobRuns
                .Where(run => run.Id == runId && run.Status == DeliveryJobRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, terminalStatus)
                    .SetProperty(run => run.CompletedAtUtc, completedAtUtc)
                    .SetProperty(run => run.UpdatedAtUtc, completedAtUtc)
                    .SetProperty(run => run.ExaminedCount, counters.ExaminedCount)
                    .SetProperty(run => run.ActivatedCount, counters.ActivatedCount)
                    .SetProperty(run => run.ExpiredCount, counters.ExpiredCount)
                    .SetProperty(run => run.CancelledCount, counters.CancelledCount)
                    .SetProperty(run => run.SkippedCount, counters.SkippedCount)
                    .SetProperty(run => run.FailedCount, counters.FailedCount)
                    .SetProperty(run => run.SafeFailureSummary, summary),
                    cancellationToken);
            return rows == 1;
        }
    }

    public Task<bool> HasCurrentDateCoverageAsync(DeliveryJobType jobType, DateOnly businessDateEgypt, CancellationToken cancellationToken = default)
    {
        return context.DeliveryJobRuns.AsNoTracking().AnyAsync(
            run => run.JobType == jobType
                && run.BusinessDateEgypt == businessDateEgypt
                && (run.Status == DeliveryJobRunStatus.Succeeded
                    || run.Status == DeliveryJobRunStatus.PartiallySucceeded),
            cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryJobRun>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        return await context.DeliveryJobRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    private static void EnsureCounters(DeliveryJobRunCounters counters)
    {
        if (new[] { counters.ExaminedCount, counters.ActivatedCount, counters.ExpiredCount, counters.CancelledCount, counters.SkippedCount, counters.FailedCount }.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(counters), "Job run counters cannot be negative.");
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

internal static class SafeSummary
{
    public static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var safe = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        foreach (var sensitiveMarker in new[]
        {
            "password=", "connectionstring=", "connection string", "access_token=", "authorization:",
            "bearer ", "signedurl=", "storagekey=", "api_key=", "cloudinary://", "http://", "https://",
            "delivery:reserve:", "delivery:release:"
        })
        {
            var index = safe.IndexOf(sensitiveMarker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                safe = safe[..index] + "[REDACTED]";
            }
        }

        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}
