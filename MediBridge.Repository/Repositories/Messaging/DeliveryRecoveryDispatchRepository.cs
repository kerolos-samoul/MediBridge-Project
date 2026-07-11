using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class DeliveryRecoveryDispatchRepository : IDeliveryRecoveryDispatchRepository
{
    private readonly MediBridgeDbContext context;

    public DeliveryRecoveryDispatchRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task<DeliveryRecoveryDispatch> FindOrCreateClaimAsync(
        DateOnly businessDateEgypt,
        DeliveryJobType jobType,
        DateTime claimedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (claimedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The claim timestamp must be UTC.", nameof(claimedAtUtc));
        }

        var dispatchId = Guid.NewGuid().ToString("N");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            IF NOT EXISTS (
                SELECT 1
                FROM [DeliveryRecoveryDispatches] WITH (UPDLOCK, HOLDLOCK)
                WHERE [BusinessDateEgypt] = {businessDateEgypt} AND [JobType] = {(int)jobType}
            )
            BEGIN
                INSERT INTO [DeliveryRecoveryDispatches]
                    ([Id], [BusinessDateEgypt], [JobType], [Status], [ClaimedAtUtc], [SchedulerJobId], [DependsOnDispatchId], [EnqueuedAtUtc], [CompletedAtUtc], [SafeFailureSummary])
                VALUES
                    ({dispatchId}, {businessDateEgypt}, {(int)jobType}, {(int)RecoveryDispatchStatus.Pending}, {claimedAtUtc}, NULL, NULL, NULL, NULL, NULL)
            END
            """, cancellationToken);

        return await context.DeliveryRecoveryDispatches.SingleAsync(
            dispatch => dispatch.BusinessDateEgypt == businessDateEgypt && dispatch.JobType == jobType,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryRecoveryDispatch>> ListPendingClaimsAsync(DateOnly businessDateEgypt, CancellationToken cancellationToken = default)
    {
        return await context.DeliveryRecoveryDispatches
            .AsNoTracking()
            .Where(dispatch => dispatch.BusinessDateEgypt == businessDateEgypt
                && (dispatch.Status == RecoveryDispatchStatus.Pending || dispatch.Status == RecoveryDispatchStatus.Failed))
            .OrderBy(dispatch => dispatch.JobType)
            .ThenBy(dispatch => dispatch.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ResetForRetryAsync(string dispatchId, DateTime claimedAtUtc, CancellationToken cancellationToken = default)
    {
        EnsureUtc(claimedAtUtc, nameof(claimedAtUtc));
        var rows = await context.DeliveryRecoveryDispatches
            .Where(dispatch => dispatch.Id == dispatchId
                && (dispatch.Status == RecoveryDispatchStatus.Pending || dispatch.Status == RecoveryDispatchStatus.Failed))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(dispatch => dispatch.Status, RecoveryDispatchStatus.Pending)
                .SetProperty(dispatch => dispatch.ClaimedAtUtc, claimedAtUtc)
                .SetProperty(dispatch => dispatch.SchedulerJobId, (string?)null)
                .SetProperty(dispatch => dispatch.DependsOnDispatchId, (string?)null)
                .SetProperty(dispatch => dispatch.EnqueuedAtUtc, (DateTime?)null)
                .SetProperty(dispatch => dispatch.CompletedAtUtc, (DateTime?)null)
                .SetProperty(dispatch => dispatch.SafeFailureSummary, (string?)null),
                cancellationToken);
        return rows == 1;
    }

    public async Task<bool> RecordEnqueuedAsync(
        string dispatchId,
        string schedulerJobId,
        string? dependsOnDispatchId,
        DateTime enqueuedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(schedulerJobId) || schedulerJobId.Length > 100)
        {
            throw new ArgumentException("A bounded scheduler job id is required.", nameof(schedulerJobId));
        }

        var dependency = string.IsNullOrWhiteSpace(dependsOnDispatchId) ? null : dependsOnDispatchId.Trim();
        if (dependency?.Length > 64)
        {
            throw new ArgumentException("The dispatch dependency id is too long.", nameof(dependsOnDispatchId));
        }

        EnsureUtc(enqueuedAtUtc, nameof(enqueuedAtUtc));

        var rows = await context.DeliveryRecoveryDispatches
            .Where(dispatch => dispatch.Id == dispatchId && dispatch.Status == RecoveryDispatchStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(dispatch => dispatch.Status, RecoveryDispatchStatus.Enqueued)
                .SetProperty(dispatch => dispatch.SchedulerJobId, schedulerJobId.Trim())
                .SetProperty(dispatch => dispatch.DependsOnDispatchId, dependency)
                .SetProperty(dispatch => dispatch.EnqueuedAtUtc, enqueuedAtUtc),
                cancellationToken);
        return rows == 1;
    }

    public async Task<bool> CompleteAsync(
        string dispatchId,
        RecoveryDispatchStatus status,
        DateTime completedAtUtc,
        string? safeFailureSummary,
        CancellationToken cancellationToken = default)
    {
        if (status is not (RecoveryDispatchStatus.Completed or RecoveryDispatchStatus.Failed))
        {
            throw new ArgumentException("A recovery dispatch completion must be Completed or Failed.", nameof(status));
        }

        EnsureUtc(completedAtUtc, nameof(completedAtUtc));

        var summary = SafeSummary.Normalize(safeFailureSummary, DeliveryRecoveryDispatch.MaxSafeFailureSummaryLength);
        var rows = await context.DeliveryRecoveryDispatches
            .Where(dispatch => dispatch.Id == dispatchId
                && (dispatch.Status == RecoveryDispatchStatus.Pending || dispatch.Status == RecoveryDispatchStatus.Enqueued))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(dispatch => dispatch.Status, status)
                .SetProperty(dispatch => dispatch.CompletedAtUtc, completedAtUtc)
                .SetProperty(dispatch => dispatch.SafeFailureSummary, summary),
                cancellationToken);
        return rows == 1;
    }

    public async Task<bool> CompleteEnqueuedForJobAsync(
        DateOnly businessDateEgypt,
        DeliveryJobType jobType,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        var rows = await context.DeliveryRecoveryDispatches
            .Where(dispatch => dispatch.BusinessDateEgypt == businessDateEgypt
                && dispatch.JobType == jobType
                && dispatch.Status == RecoveryDispatchStatus.Enqueued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(dispatch => dispatch.Status, RecoveryDispatchStatus.Completed)
                .SetProperty(dispatch => dispatch.CompletedAtUtc, completedAtUtc)
                .SetProperty(dispatch => dispatch.SafeFailureSummary, (string?)null),
                cancellationToken);
        return rows == 1;
    }

    public async Task<IReadOnlyList<DeliveryRecoveryDispatch>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        return await context.DeliveryRecoveryDispatches
            .AsNoTracking()
            .OrderByDescending(dispatch => dispatch.ClaimedAtUtc)
            .ThenByDescending(dispatch => dispatch.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Operational timestamps must be UTC.", parameterName);
        }
    }
}
