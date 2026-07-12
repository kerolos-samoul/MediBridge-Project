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

public sealed class WeeklyEnforcementService : IWeeklyEnforcementService
{
    private const int PageSize = 100;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IEgyptBusinessClock businessClock;
    private readonly IActivityScoreService activityScoreService;
    private readonly ActivityEnforcementJobRunTracker jobRunTracker;
    private readonly ILogger<WeeklyEnforcementService> logger;

    public WeeklyEnforcementService(
        IDomainUnitOfWork domainUnitOfWork,
        IEgyptBusinessClock businessClock,
        IActivityScoreService activityScoreService,
        WeeklyEnforcementPolicy policy,
        ActivityEnforcementJobRunTracker jobRunTracker,
        ILogger<WeeklyEnforcementService> logger)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.businessClock = businessClock;
        this.activityScoreService = activityScoreService;
        _ = policy;
        this.jobRunTracker = jobRunTracker;
        this.logger = logger;
    }

    public async Task<ActivityJobRunDto> RunWeeklyEnforcementAsync(DateOnly? weekStartDateEgypt, string? requestedByAdminUserId, CancellationToken cancellationToken)
    {
        var snapshot = businessClock.Capture();
        var window = weekStartDateEgypt.HasValue
            ? WeeklyEnforcementPolicy.FromWeekStart(weekStartDateEgypt.Value)
            : WeeklyEnforcementPolicy.DeriveLastCompletedWeek(snapshot.BusinessDateEgypt);
        var handle = await jobRunTracker.StartAsync(
            ActivityEnforcementJobType.WeeklyEnforcement,
            targetScoreDateEgypt: null,
            window.WeekStartDateEgypt,
            requestedByAdminUserId,
            snapshot.UtcNow,
            cancellationToken);
        var progress = new WeeklyProgress();
        var status = ActivityEnforcementJobRunStatus.Succeeded;
        string? safeFailureSummary = null;

        try
        {
            await activityScoreService.ExpireSuspensionsAsync(requestedByAdminUserId, cancellationToken);
            string? cursor = null;
            while (true)
            {
                var doctorIds = await domainUnitOfWork.Profiles.ListApprovedNonDeletedDoctorIdsForWeeklyEnforcementAsync(
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
                    var outcome = await ProcessCandidateAsync(doctorId, window, handle.JobRunId, snapshot.UtcNow, cancellationToken);
                    switch (outcome)
                    {
                        case WeeklyCandidateOutcome.Created:
                            progress.CreatedCount++;
                            break;
                        case WeeklyCandidateOutcome.Replayed:
                            progress.UpdatedCount++;
                            break;
                        case WeeklyCandidateOutcome.Skipped:
                            progress.SkippedCount++;
                            break;
                        case WeeklyCandidateOutcome.Failed:
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
            safeFailureSummary = progress.FailedCount == 0 ? null : "One or more weekly enforcement candidates failed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = ActivityEnforcementJobRunStatus.Interrupted;
            safeFailureSummary = "The weekly enforcement run was interrupted.";
            await jobRunTracker.CompleteAsync(handle, status, progress.ToCounters(), snapshot.UtcNow, safeFailureSummary, CancellationToken.None);
            logger.LogWarning("Weekly enforcement run {RunId} was interrupted for {WeekStartDateEgypt}.", handle.JobRunId, window.WeekStartDateEgypt);
            throw;
        }
        catch (Exception exception)
        {
            progress.FailedCount++;
            status = ActivityEnforcementJobRunStatus.Failed;
            safeFailureSummary = "The weekly enforcement run failed.";
            logger.LogWarning(exception, "Weekly enforcement run {RunId} failed for {WeekStartDateEgypt}.", handle.JobRunId, window.WeekStartDateEgypt);
        }

        await jobRunTracker.CompleteAsync(handle, status, progress.ToCounters(), snapshot.UtcNow, safeFailureSummary, cancellationToken);
        logger.LogInformation(
            "Weekly enforcement run {RunId} completed for {WeekStartDateEgypt} with {Status}; processed {ProcessedCount}, skipped {SkippedCount}, created {CreatedCount}, updated {UpdatedCount}, failed {FailedCount}.",
            handle.JobRunId,
            window.WeekStartDateEgypt,
            status,
            progress.ProcessedCount,
            progress.SkippedCount,
            progress.CreatedCount,
            progress.UpdatedCount,
            progress.FailedCount);

        return ActivityEnforcementJobRunTracker.ToDto(handle, status, progress.ToCounters(), safeFailureSummary);
    }

    private async Task<WeeklyCandidateOutcome> ProcessCandidateAsync(
        string doctorId,
        WeeklyEnforcementWindow window,
        string jobRunId,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
            {
                var existing = await domainUnitOfWork.WeeklyEnforcement.FindDecisionAsync(
                    doctorId,
                    window.WeekStartDateEgypt,
                    transactionCancellationToken);
                if (existing is not null)
                {
                    return WeeklyCandidateOutcome.Replayed;
                }

                var profile = await domainUnitOfWork.Profiles.FindDoctorProfileForEnforcementUpdateByIdAsync(doctorId, transactionCancellationToken);
                if (profile is null)
                {
                    return WeeklyCandidateOutcome.Skipped;
                }

                var suspensionOverlapped = await domainUnitOfWork.Profiles.SuspensionOverlapsEgyptWeekAsync(
                    doctorId,
                    window.WeekStartDateEgypt,
                    window.WeekEndDateEgypt,
                    transactionCancellationToken);
                var weeklyCount = await domainUnitOfWork.Deliveries.GetWeeklyInteractionCountAsync(
                    doctorId,
                    window.WeekStartDateEgypt,
                    window.WeekEndDateEgypt,
                    transactionCancellationToken);
                var decisionType = WeeklyEnforcementPolicy.ClassifyDecision(
                    profile.MinimumWeeklyRequirement,
                    weeklyCount.InteractionCount,
                    suspensionOverlapped);
                var rollingStart = WeeklyEnforcementPolicy.GetRollingWindowStart(window.WeekStartDateEgypt);
                var priorRollingCount = await domainUnitOfWork.WeeklyEnforcement.CountRollingViolationsAsync(
                    doctorId,
                    rollingStart,
                    window.WeekStartDateEgypt.AddDays(-7),
                    transactionCancellationToken);
                var rollingAfterDecision = decisionType == WeeklyEnforcementDecisionType.Violation
                    ? priorRollingCount + 1
                    : priorRollingCount;
                var decision = new WeeklyEnforcementDecision
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DoctorId = doctorId,
                    WeekStartDateEgypt = window.WeekStartDateEgypt,
                    WeekEndDateEgypt = window.WeekEndDateEgypt,
                    MinimumWeeklyRequirement = profile.MinimumWeeklyRequirement,
                    InteractionCount = weeklyCount.InteractionCount,
                    Decision = decisionType,
                    SuspensionOverlapped = suspensionOverlapped,
                    RollingViolationCountAfterDecision = rollingAfterDecision,
                    CreatedAtUtc = createdAtUtc,
                    JobRunId = jobRunId
                };
                await domainUnitOfWork.WeeklyEnforcement.AddDecisionAsync(decision, transactionCancellationToken);

                if (decisionType == WeeklyEnforcementDecisionType.Violation)
                {
                    var auditEventId = Guid.NewGuid().ToString("N");
                    await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                        auditEventId,
                        "Phase9WeeklyViolationRecorded",
                        actorUserId: null,
                        actorRole: null,
                        AuditTargetType.Doctor,
                        doctorId,
                        AuditOutcome.Info,
                        "Weekly interaction minimum was missed.",
                        correlationId: null,
                        JsonSerializer.Serialize(new
                        {
                            DoctorId = doctorId,
                            window.WeekStartDateEgypt,
                            window.WeekEndDateEgypt,
                            profile.MinimumWeeklyRequirement,
                            weeklyCount.InteractionCount,
                            RollingViolationCount = rollingAfterDecision
                        }),
                        createdAtUtc,
                        transactionCancellationToken);
                    await domainUnitOfWork.WeeklyEnforcement.AddViolationAsync(new DoctorWeeklyViolation
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        DoctorId = doctorId,
                        WeeklyEnforcementDecisionId = decision.Id,
                        WeekStartDateEgypt = window.WeekStartDateEgypt,
                        WeekEndDateEgypt = window.WeekEndDateEgypt,
                        MinimumWeeklyRequirement = profile.MinimumWeeklyRequirement,
                        InteractionCount = weeklyCount.InteractionCount,
                        RollingViolationCount = rollingAfterDecision,
                        CreatedAtUtc = createdAtUtc,
                        AuditEventId = auditEventId
                    }, transactionCancellationToken);
                }

                return WeeklyCandidateOutcome.Created;
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            var replay = await domainUnitOfWork.WeeklyEnforcement.FindDecisionAsync(doctorId, window.WeekStartDateEgypt, cancellationToken);
            return replay is null ? WeeklyCandidateOutcome.Failed : WeeklyCandidateOutcome.Replayed;
        }
    }

    private sealed class WeeklyProgress
    {
        public int ProcessedCount { get; set; }
        public int SkippedCount { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int FailedCount { get; set; }

        public ActivityJobRunCounters ToCounters() => new(ProcessedCount, SkippedCount, CreatedCount, UpdatedCount, FailedCount);
    }

    private enum WeeklyCandidateOutcome
    {
        Created,
        Replayed,
        Skipped,
        Failed
    }
}
