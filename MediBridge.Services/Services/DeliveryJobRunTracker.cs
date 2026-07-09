using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using Microsoft.Extensions.Logging;

namespace MediBridge.Services.Services;

public sealed record DeliveryJobRunHandle(
    string RunId,
    DeliveryJobType JobType,
    DateOnly BusinessDateEgypt,
    DateTime StartedAtUtc);

internal sealed class DeliveryJobRunProgress
{
    public int ExaminedCount { get; private set; }
    public int ActivatedCount { get; private set; }
    public int ExpiredCount { get; private set; }
    public int CancelledCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int FailedCount { get; private set; }

    public void RecordExamined() => ExaminedCount++;
    public void RecordActivated() => ActivatedCount++;
    public void RecordExpired() => ExpiredCount++;
    public void RecordCancelled() => CancelledCount++;
    public void RecordSkipped() => SkippedCount++;
    public void RecordFailed() => FailedCount++;

    public DeliveryJobRunCounters ToCounters() => new(
        ExaminedCount,
        ActivatedCount,
        ExpiredCount,
        CancelledCount,
        SkippedCount,
        FailedCount);
}

public sealed class DeliveryJobRunTracker
{
    private static readonly TimeSpan StaleRunAge = TimeSpan.FromDays(1);
    private readonly IDomainUnitOfWork unitOfWork;
    private readonly ILogger<DeliveryJobRunTracker> logger;

    public DeliveryJobRunTracker(IDomainUnitOfWork unitOfWork, ILogger<DeliveryJobRunTracker> logger)
    {
        this.unitOfWork = unitOfWork;
        this.logger = logger;
    }

    public async Task<DeliveryJobRunHandle> StartAsync(
        DeliveryJobType jobType,
        DateOnly businessDateEgypt,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        var run = new DeliveryJobRun
        {
            Id = Guid.NewGuid().ToString("N"),
            JobType = jobType,
            BusinessDateEgypt = businessDateEgypt,
            Status = DeliveryJobRunStatus.Running,
            StartedAtUtc = startedAtUtc,
            CreatedAtUtc = startedAtUtc
        };

        await unitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            await unitOfWork.DeliveryJobRuns.InterruptStaleRunningAsync(
                jobType,
                startedAtUtc.Subtract(StaleRunAge),
                startedAtUtc,
                transactionCancellationToken);
            await unitOfWork.DeliveryJobRuns.AddRunningAsync(run, transactionCancellationToken);
        }, cancellationToken);

        logger.LogInformation(
            "Delivery job run {RunId} started for {JobType} on {BusinessDateEgypt}.",
            run.Id,
            jobType,
            businessDateEgypt);
        return new DeliveryJobRunHandle(run.Id, jobType, businessDateEgypt, startedAtUtc);
    }

    public async Task CompleteAsync(
        DeliveryJobRunHandle handle,
        DeliveryJobRunStatus status,
        DeliveryJobRunCounters counters,
        DateTime completedAtUtc,
        string? safeFailureSummary,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var completed = await unitOfWork.DeliveryJobRuns.CompleteAsync(
                handle.RunId,
                status,
                counters,
                completedAtUtc,
                safeFailureSummary,
                transactionCancellationToken);
            if (!completed)
            {
                throw new InvalidOperationException("The delivery job run could not be completed exactly once.");
            }

            await unitOfWork.DeliveryRecoveryDispatches.CompleteEnqueuedForJobAsync(
                handle.BusinessDateEgypt,
                handle.JobType,
                completedAtUtc,
                transactionCancellationToken);
        }, cancellationToken);

        logger.LogInformation(
            "Delivery job run {RunId} completed for {JobType} on {BusinessDateEgypt} with {Status}; examined {ExaminedCount}, activated {ActivatedCount}, expired {ExpiredCount}, cancelled {CancelledCount}, skipped {SkippedCount}, failed {FailedCount}.",
            handle.RunId,
            handle.JobType,
            handle.BusinessDateEgypt,
            status,
            counters.ExaminedCount,
            counters.ActivatedCount,
            counters.ExpiredCount,
            counters.CancelledCount,
            counters.SkippedCount,
            counters.FailedCount);
    }
}
