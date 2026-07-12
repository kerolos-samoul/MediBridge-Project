using System.Text.Json;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace MediBridge.Services.Services;

public sealed class ActivityScoreService : IActivityScoreService
{
    private const int PageSize = 100;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IEgyptBusinessClock businessClock;
    private readonly ActivityScoreCalculator calculator;
    private readonly ActivityEnforcementJobRunTracker jobRunTracker;
    private readonly ILogger<ActivityScoreService> logger;

    public ActivityScoreService(
        IDomainUnitOfWork domainUnitOfWork,
        IEgyptBusinessClock businessClock,
        ActivityScoreCalculator calculator,
        ActivityEnforcementJobRunTracker jobRunTracker,
        ILogger<ActivityScoreService> logger)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.businessClock = businessClock;
        this.calculator = calculator;
        this.jobRunTracker = jobRunTracker;
        this.logger = logger;
    }

    public async Task<ActivityJobRunDto> RunDailyScoreAsync(DateOnly? scoreDateEgypt, string? requestedByAdminUserId, CancellationToken cancellationToken)
    {
        _ = calculator;
        var snapshot = businessClock.Capture();
        var targetScoreDate = scoreDateEgypt ?? snapshot.BusinessDateEgypt;
        var handle = await jobRunTracker.StartAsync(
            ActivityEnforcementJobType.DailyActivityScore,
            targetScoreDate,
            targetWeekStartDateEgypt: null,
            requestedByAdminUserId,
            snapshot.UtcNow,
            cancellationToken);

        var progress = new ActivityScoreProgress();
        string? safeFailureSummary = null;
        ActivityEnforcementJobRunStatus status;
        try
        {
            await ExpireSuspensionsAsync(requestedByAdminUserId, cancellationToken);
            string? cursor = null;
            while (true)
            {
                var doctorIds = await domainUnitOfWork.Profiles.ListApprovedNonDeletedDoctorIdsForActivityScoringAsync(
                    cursor,
                    PageSize,
                    cancellationToken);
                if (doctorIds.Count == 0)
                {
                    break;
                }

                foreach (var doctorId in doctorIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.ProcessedCount++;
                    var outcome = await ProcessScoreCandidateAsync(doctorId, targetScoreDate, handle.JobRunId, snapshot.UtcNow, cancellationToken);
                    switch (outcome)
                    {
                        case ScoreCandidateOutcome.Created:
                            progress.CreatedCount++;
                            break;
                        case ScoreCandidateOutcome.Replayed:
                            progress.UpdatedCount++;
                            break;
                        case ScoreCandidateOutcome.Skipped:
                            progress.SkippedCount++;
                            break;
                        case ScoreCandidateOutcome.Failed:
                            progress.FailedCount++;
                            break;
                    }
                }

                cursor = doctorIds[^1];
                if (doctorIds.Count < PageSize)
                {
                    break;
                }
            }

            status = ActivityEnforcementJobRunTracker.Classify(progress.CreatedCount + progress.UpdatedCount, progress.FailedCount);
            safeFailureSummary = progress.FailedCount == 0 ? null : "One or more activity score candidates failed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = ActivityEnforcementJobRunStatus.Interrupted;
            safeFailureSummary = "The activity score run was interrupted.";
            await jobRunTracker.CompleteAsync(handle, status, progress.ToCounters(), snapshot.UtcNow, safeFailureSummary, CancellationToken.None);
            logger.LogWarning("Activity score run {RunId} was interrupted for {ScoreDateEgypt}.", handle.JobRunId, targetScoreDate);
            throw;
        }
        catch (Exception exception)
        {
            progress.FailedCount++;
            status = ActivityEnforcementJobRunStatus.Failed;
            safeFailureSummary = "The activity score run failed.";
            logger.LogWarning(exception, "Activity score run {RunId} failed for {ScoreDateEgypt}.", handle.JobRunId, targetScoreDate);
        }

        await jobRunTracker.CompleteAsync(handle, status, progress.ToCounters(), snapshot.UtcNow, safeFailureSummary, cancellationToken);
        logger.LogInformation(
            "Activity score run {RunId} completed for {ScoreDateEgypt} with {Status}; processed {ProcessedCount}, skipped {SkippedCount}, created {CreatedCount}, updated {UpdatedCount}, failed {FailedCount}.",
            handle.JobRunId,
            targetScoreDate,
            status,
            progress.ProcessedCount,
            progress.SkippedCount,
            progress.CreatedCount,
            progress.UpdatedCount,
            progress.FailedCount);

        return ActivityEnforcementJobRunTracker.ToDto(handle, status, progress.ToCounters(), safeFailureSummary);
    }

    public async Task<ActivityJobRunDto> ExpireSuspensionsAsync(string? requestedByAdminUserId, CancellationToken cancellationToken)
    {
        var snapshot = businessClock.Capture();
        var handle = await jobRunTracker.StartAsync(
            ActivityEnforcementJobType.SuspensionExpiry,
            targetScoreDateEgypt: null,
            targetWeekStartDateEgypt: null,
            requestedByAdminUserId,
            snapshot.UtcNow,
            cancellationToken);
        var progress = new ActivityScoreProgress();

        string? safeFailureSummary = null;
        var status = ActivityEnforcementJobRunStatus.Succeeded;
        try
        {
            while (true)
            {
                var doctors = await domainUnitOfWork.Profiles.ListExpiredSuspendedDoctorsForUpdateAsync(snapshot.UtcNow, PageSize, cancellationToken);
                if (doctors.Count == 0)
                {
                    break;
                }

                foreach (var doctor in doctors)
                {
                    progress.ProcessedCount++;
                    var reactivated = await ProcessSuspensionExpiryCandidateAsync(doctor.Id, snapshot.UtcNow, requestedByAdminUserId, cancellationToken);
                    if (reactivated)
                    {
                        progress.UpdatedCount++;
                    }
                    else
                    {
                        progress.SkippedCount++;
                    }
                }

                if (doctors.Count < PageSize)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = ActivityEnforcementJobRunStatus.Interrupted;
            safeFailureSummary = "The suspension expiry run was interrupted.";
            await jobRunTracker.CompleteAsync(handle, status, progress.ToCounters(), snapshot.UtcNow, safeFailureSummary, CancellationToken.None);
            logger.LogWarning("Suspension expiry run {RunId} was interrupted.", handle.JobRunId);
            throw;
        }
        catch (Exception exception)
        {
            progress.FailedCount++;
            status = ActivityEnforcementJobRunStatus.Failed;
            safeFailureSummary = "The suspension expiry run failed.";
            logger.LogWarning(exception, "Suspension expiry run {RunId} failed.", handle.JobRunId);
        }

        if (status != ActivityEnforcementJobRunStatus.Failed)
        {
            status = ActivityEnforcementJobRunTracker.Classify(progress.UpdatedCount, progress.FailedCount);
        }

        await jobRunTracker.CompleteAsync(handle, status, progress.ToCounters(), snapshot.UtcNow, safeFailureSummary, cancellationToken);
        logger.LogInformation(
            "Suspension expiry run {RunId} completed with {Status}; processed {ProcessedCount}, skipped {SkippedCount}, updated {UpdatedCount}, failed {FailedCount}.",
            handle.JobRunId,
            status,
            progress.ProcessedCount,
            progress.SkippedCount,
            progress.UpdatedCount,
            progress.FailedCount);
        return ActivityEnforcementJobRunTracker.ToDto(handle, status, progress.ToCounters(), safeFailureSummary);
    }

    private async Task<ScoreCandidateOutcome> ProcessScoreCandidateAsync(
        string doctorId,
        DateOnly scoreDateEgypt,
        string jobRunId,
        DateTime calculatedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessScoreCandidateCoreAsync(doctorId, scoreDateEgypt, jobRunId, calculatedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            try
            {
                return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
                {
                    var replay = await domainUnitOfWork.ActivityScoreHistories.FindByDoctorAndDateAsync(doctorId, scoreDateEgypt, transactionCancellationToken);
                    if (replay is null)
                    {
                        return ScoreCandidateOutcome.Failed;
                    }

                    var updated = await domainUnitOfWork.Profiles.ApplyCurrentActivityScoreAsync(
                        doctorId,
                        replay.FinalScore,
                        calculatedAtUtc,
                        transactionCancellationToken);
                    return updated ? ScoreCandidateOutcome.Replayed : ScoreCandidateOutcome.Skipped;
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return ScoreCandidateOutcome.Failed;
            }
        }
    }

    private Task<ScoreCandidateOutcome> ProcessScoreCandidateCoreAsync(
        string doctorId,
        DateOnly scoreDateEgypt,
        string jobRunId,
        DateTime calculatedAtUtc,
        CancellationToken cancellationToken)
    {
        return domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var existing = await domainUnitOfWork.ActivityScoreHistories.FindByDoctorAndDateAsync(doctorId, scoreDateEgypt, transactionCancellationToken);
            if (existing is not null)
            {
                var updated = await domainUnitOfWork.Profiles.ApplyCurrentActivityScoreAsync(
                    doctorId,
                    existing.FinalScore,
                    calculatedAtUtc,
                    transactionCancellationToken);
                return updated ? ScoreCandidateOutcome.Replayed : ScoreCandidateOutcome.Skipped;
            }

            var profile = await domainUnitOfWork.Profiles.FindDoctorProfileForEnforcementUpdateByIdAsync(doctorId, transactionCancellationToken);
            if (profile is null)
            {
                return ScoreCandidateOutcome.Skipped;
            }

            var window = ActivityScoreCalculator.DeriveWindow(scoreDateEgypt);
            var aggregate = await domainUnitOfWork.Deliveries.GetActivityScoreAggregateAsync(
                doctorId,
                window.WindowStartDateEgypt,
                window.WindowEndDateEgypt,
                transactionCancellationToken);
            var result = ActivityScoreCalculator.Calculate(aggregate);
            var snapshot = new ActivityScoreHistory
            {
                Id = Guid.NewGuid().ToString("N"),
                DoctorId = doctorId,
                ScoreDateEgypt = scoreDateEgypt,
                WindowStartDateEgypt = window.WindowStartDateEgypt,
                WindowEndDateEgypt = window.WindowEndDateEgypt,
                DeliveredCount = aggregate.DeliveredCount,
                InteractedCount = aggregate.InteractedCount,
                FeedbackQualifiedCount = aggregate.FeedbackQualifiedCount,
                ResponseSpeedScore = result.ResponseSpeedScore,
                EngagementScore = result.EngagementScore,
                FeedbackScore = result.FeedbackScore,
                FinalScore = result.FinalScore,
                CalculationMode = result.CalculationMode,
                DoctorWasSuspended = profile.Status == DoctorMarketplaceStatus.Suspended,
                CalculatedAtUtc = calculatedAtUtc,
                CreatedAtUtc = calculatedAtUtc,
                JobRunId = jobRunId
            };
            await domainUnitOfWork.ActivityScoreHistories.AddAsync(snapshot, transactionCancellationToken);
            profile.ApplyActivityScore(result.FinalScore, calculatedAtUtc);
            await domainUnitOfWork.Profiles.ApplyEnforcementStateChangesAsync(profile, transactionCancellationToken);
            return ScoreCandidateOutcome.Created;
        }, cancellationToken);
    }

    private Task<bool> ProcessSuspensionExpiryCandidateAsync(
        string doctorId,
        DateTime nowUtc,
        string? requestedByAdminUserId,
        CancellationToken cancellationToken)
    {
        return domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var profile = await domainUnitOfWork.Profiles.FindDoctorProfileForEnforcementUpdateByIdAsync(doctorId, transactionCancellationToken);
            if (profile is not { Status: DoctorMarketplaceStatus.Suspended, SuspendedUntilUtc: not null }
                || profile.SuspendedUntilUtc > nowUtc)
            {
                return false;
            }

            var suspendedUntilUtc = NormalizePersistedUtc(profile.SuspendedUntilUtc.Value);
            if (await domainUnitOfWork.DoctorEnforcementActions.AutomaticReactivationExistsAsync(
                doctorId,
                suspendedUntilUtc,
                transactionCancellationToken))
            {
                profile.Reactivate(nowUtc);
                await domainUnitOfWork.Profiles.ApplyEnforcementStateChangesAsync(profile, transactionCancellationToken);
                return false;
            }

            var previousStatus = profile.Status;
            var previousLimit = profile.DailyMessageLimit;
            profile.Reactivate(nowUtc);
            await domainUnitOfWork.Profiles.ApplyEnforcementStateChangesAsync(profile, transactionCancellationToken);
            var auditEventId = Guid.NewGuid().ToString("N");
            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                auditEventId,
                "Phase9AutomaticDoctorReactivated",
                requestedByAdminUserId,
                requestedByAdminUserId is null ? null : UserRole.Admin.ToString(),
                AuditTargetType.Doctor,
                doctorId,
                AuditOutcome.Info,
                "Temporary suspension expired.",
                correlationId: null,
                JsonSerializer.Serialize(new { DoctorId = doctorId, SuspendedUntilUtc = suspendedUntilUtc }),
                nowUtc,
                transactionCancellationToken);
            await domainUnitOfWork.DoctorEnforcementActions.AddAsync(new DoctorEnforcementAction
            {
                Id = Guid.NewGuid().ToString("N"),
                DoctorId = doctorId,
                ActionType = DoctorEnforcementActionType.AutomaticReactivate,
                PreviousStatus = previousStatus,
                NewStatus = DoctorMarketplaceStatus.Active,
                PreviousDailyMessageLimit = previousLimit,
                SuspendedUntilUtc = suspendedUntilUtc,
                EffectiveAtUtc = nowUtc,
                AuditEventId = auditEventId,
                CreatedAtUtc = nowUtc,
                Reason = "Temporary suspension expired."
            }, transactionCancellationToken);
            return true;
        }, cancellationToken);
    }

    private static DateTime NormalizePersistedUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private sealed class ActivityScoreProgress
    {
        public int ProcessedCount { get; set; }
        public int SkippedCount { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int FailedCount { get; set; }

        public ActivityJobRunCounters ToCounters() => new(ProcessedCount, SkippedCount, CreatedCount, UpdatedCount, FailedCount);
    }

    private enum ScoreCandidateOutcome
    {
        Created,
        Replayed,
        Skipped,
        Failed
    }
}
