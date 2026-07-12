using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class ActivityEnforcementJobRunTracker
{
    private static readonly TimeSpan StaleRunAge = TimeSpan.FromDays(1);
    private readonly IDomainUnitOfWork domainUnitOfWork;

    public ActivityEnforcementJobRunTracker(IDomainUnitOfWork domainUnitOfWork)
    {
        this.domainUnitOfWork = domainUnitOfWork;
    }

    public async Task<ActivityEnforcementJobRunHandle> StartAsync(
        ActivityEnforcementJobType jobType,
        DateOnly? targetScoreDateEgypt,
        DateOnly? targetWeekStartDateEgypt,
        string? requestedByAdminUserId,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        var run = new ActivityEnforcementJobRun
        {
            Id = Guid.NewGuid().ToString("N"),
            JobType = jobType,
            TargetScoreDateEgypt = targetScoreDateEgypt,
            TargetWeekStartDateEgypt = targetWeekStartDateEgypt,
            Status = ActivityEnforcementJobRunStatus.Running,
            StartedAtUtc = startedAtUtc,
            CreatedAtUtc = startedAtUtc,
            RequestedByAdminUserId = requestedByAdminUserId
        };

        await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            await domainUnitOfWork.ActivityEnforcementJobRuns.InterruptStaleRunningAsync(
                jobType,
                startedAtUtc.Subtract(StaleRunAge),
                startedAtUtc,
                transactionCancellationToken);
            await domainUnitOfWork.ActivityEnforcementJobRuns.AddRunningAsync(run, transactionCancellationToken);
        }, cancellationToken);

        return new ActivityEnforcementJobRunHandle(run.Id, jobType, targetScoreDateEgypt, targetWeekStartDateEgypt);
    }

    public Task CompleteAsync(
        ActivityEnforcementJobRunHandle handle,
        ActivityEnforcementJobRunStatus status,
        ActivityJobRunCounters counters,
        DateTime completedAtUtc,
        string? safeFailureSummary,
        CancellationToken cancellationToken)
    {
        return domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var completed = await domainUnitOfWork.ActivityEnforcementJobRuns.CompleteAsync(
                handle.JobRunId,
                status,
                counters,
                completedAtUtc,
                safeFailureSummary,
                transactionCancellationToken);
            if (!completed)
            {
                throw new Phase9ConflictException("The activity enforcement job run could not be completed.");
            }
        }, cancellationToken);
    }

    public static ActivityEnforcementJobRunStatus Classify(int successfulCount, int failedCount)
    {
        if (failedCount == 0)
        {
            return ActivityEnforcementJobRunStatus.Succeeded;
        }

        return successfulCount > 0
            ? ActivityEnforcementJobRunStatus.PartiallySucceeded
            : ActivityEnforcementJobRunStatus.Failed;
    }

    public static ActivityJobRunDto ToDto(
        ActivityEnforcementJobRunHandle handle,
        ActivityEnforcementJobRunStatus status,
        ActivityJobRunCounters counters,
        string? safeFailureSummary)
    {
        return new ActivityJobRunDto(
            handle.JobRunId,
            handle.JobType,
            handle.TargetScoreDateEgypt,
            handle.TargetWeekStartDateEgypt,
            status,
            counters.ProcessedCount,
            counters.SkippedCount,
            counters.CreatedCount,
            counters.UpdatedCount,
            counters.FailedCount,
            safeFailureSummary);
    }
}

public sealed record ActivityEnforcementJobRunHandle(
    string JobRunId,
    ActivityEnforcementJobType JobType,
    DateOnly? TargetScoreDateEgypt,
    DateOnly? TargetWeekStartDateEgypt);
