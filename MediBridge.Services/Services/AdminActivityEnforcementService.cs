using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Profiles;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class AdminActivityEnforcementService : IAdminActivityEnforcementService
{
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IActivityScoreService activityScoreService;
    private readonly IWeeklyEnforcementService weeklyEnforcementService;
    private readonly IEgyptBusinessClock businessClock;
    private readonly IValidator<DoctorEnforcementActionRequestDto> actionValidator;
    private readonly IValidator<RunDailyActivityScoreRequestDto> dailyScoreValidator;
    private readonly IValidator<RunWeeklyEnforcementRequestDto> weeklyEnforcementValidator;

    public AdminActivityEnforcementService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IActivityScoreService activityScoreService,
        IWeeklyEnforcementService weeklyEnforcementService,
        IEgyptBusinessClock businessClock,
        IValidator<DoctorEnforcementActionRequestDto> actionValidator,
        IValidator<RunDailyActivityScoreRequestDto> dailyScoreValidator,
        IValidator<RunWeeklyEnforcementRequestDto> weeklyEnforcementValidator)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.activityScoreService = activityScoreService;
        this.weeklyEnforcementService = weeklyEnforcementService;
        this.businessClock = businessClock;
        this.actionValidator = actionValidator;
        this.dailyScoreValidator = dailyScoreValidator;
        this.weeklyEnforcementValidator = weeklyEnforcementValidator;
    }

    public async Task<ViolationSummaryPageDto> ListViolationsAsync(ViolationSummaryQuery query, string adminUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, forUpdate: false, cancellationToken);
        var criteria = NormalizeViolationCriteria(query);

        var expiry = await activityScoreService.ExpireSuspensionsAsync(adminUserId, cancellationToken);
        if (expiry.Status is ActivityEnforcementJobRunStatus.Failed or ActivityEnforcementJobRunStatus.PartiallySucceeded)
        {
            throw new Phase9ServiceUnavailableException("Suspension expiry could not be completed.");
        }

        var page = await domainUnitOfWork.WeeklyEnforcement.ListViolationSummariesAsync(
            criteria,
            query.PageNumber,
            query.PageSize,
            cancellationToken);

        var totalPages = page.TotalCount == 0 ? 0 : (int)Math.Ceiling(page.TotalCount / (double)page.PageSize);
        return new ViolationSummaryPageDto(
            page.Items.Select(item => new ViolationSummaryDto(
                item.DoctorId,
                item.DoctorDisplayName,
                item.Status,
                item.DailyMessageLimit,
                item.MinimumWeeklyRequirement,
                item.ActivityScore,
                item.SuspendedUntilUtc,
                item.RollingViolationCount,
                WeeklyEnforcementPolicy.DetermineEligibility(item.RollingViolationCount),
                item.RecentViolationWeeks,
                item.LastEnforcementAction is null
                    ? null
                    : new LastEnforcementActionDto(item.LastEnforcementAction.ActionType, item.LastEnforcementAction.EffectiveAtUtc, item.LastEnforcementAction.Reason))).ToArray(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount,
            totalPages,
            page.PageNumber > 1,
            totalPages > 0 && page.PageNumber < totalPages);
    }

    public async Task<DoctorEnforcementActionResultDto> ApplyDoctorEnforcementActionAsync(
        string doctorId,
        DoctorEnforcementActionRequestDto request,
        string adminUserId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(doctorId))
        {
            throw new Phase9NotFoundException("Doctor was not found.");
        }

        var validation = await actionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        var admin = await EnsureAdminAsync(adminUserId, forUpdate: true, cancellationToken);
        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var profile = await domainUnitOfWork.Profiles.FindDoctorProfileForEnforcementUpdateByIdAsync(
                doctorId.Trim(),
                transactionCancellationToken)
                ?? throw new Phase9NotFoundException("Doctor was not found.");
            var doctorUser = await identityUnitOfWork.Users.FindByIdForUpdateAsync(profile.UserId, transactionCancellationToken);
            if (doctorUser is not { Role: UserRole.Doctor, AccountStatus: AccountStatus.Approved, IsDeleted: false })
            {
                throw new Phase9NotFoundException("Doctor was not found.");
            }

            var now = businessClock.Capture().UtcNow;
            var previousStatus = profile.Status;
            var previousLimit = profile.DailyMessageLimit;
            var actionType = request.ActionType!.Value;
            var reason = request.Reason!.Trim();

            switch (actionType)
            {
                case DoctorEnforcementActionType.Warn:
                    if (profile.Status == DoctorMarketplaceStatus.Suspended)
                    {
                        throw new Phase9ConflictException("Suspended doctors must be reactivated before warning.");
                    }

                    profile.ApplyWarning(now);
                    break;
                case DoctorEnforcementActionType.ReduceDailyLimit:
                    profile.ReduceDailyLimit(request.NewDailyMessageLimit!.Value, now);
                    break;
                case DoctorEnforcementActionType.Suspend:
                    profile.SuspendUntil(now, request.SuspendedUntilUtc!.Value);
                    break;
                case DoctorEnforcementActionType.Reactivate:
                    if (profile.Status != DoctorMarketplaceStatus.Suspended)
                    {
                        throw new Phase9ConflictException("Doctor is not suspended.");
                    }
                    profile.Reactivate(now);
                    break;
                default:
                    throw new Phase9ValidationException("Validation failed.");
            }

            await domainUnitOfWork.Profiles.ApplyEnforcementStateChangesAsync(profile, transactionCancellationToken);
            var auditEventId = Guid.NewGuid().ToString("N");
            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                auditEventId,
                "Phase9DoctorEnforcementActionApplied",
                admin.Id,
                admin.Role.ToString(),
                AuditTargetType.Doctor,
                profile.Id,
                AuditOutcome.Success,
                reason,
                correlationId,
                JsonSerializer.Serialize(new
                {
                    ActionType = actionType.ToString(),
                    PreviousStatus = previousStatus.ToString(),
                    NewStatus = profile.Status.ToString(),
                    PreviousDailyMessageLimit = previousLimit,
                    NewDailyMessageLimit = profile.DailyMessageLimit,
                    profile.SuspendedUntilUtc
                }),
                now,
                transactionCancellationToken);
            await domainUnitOfWork.DoctorEnforcementActions.AddAsync(new DoctorEnforcementAction
            {
                Id = Guid.NewGuid().ToString("N"),
                DoctorId = profile.Id,
                ActorAdminUserId = admin.Id,
                ActionType = actionType,
                Reason = reason,
                PreviousStatus = previousStatus,
                NewStatus = profile.Status,
                PreviousDailyMessageLimit = previousLimit,
                NewDailyMessageLimit = actionType == DoctorEnforcementActionType.ReduceDailyLimit ? profile.DailyMessageLimit : null,
                SuspendedAtUtc = profile.SuspendedAtUtc,
                SuspendedUntilUtc = profile.SuspendedUntilUtc,
                EffectiveAtUtc = now,
                CorrelationId = correlationId,
                AuditEventId = auditEventId,
                CreatedAtUtc = now
            }, transactionCancellationToken);

            return new DoctorEnforcementActionResultDto(
                profile.Id,
                profile.Status,
                profile.DailyMessageLimit,
                profile.SuspendedUntilUtc,
                actionType,
                now,
                auditEventId);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityJobRunDto>> ListJobStatusAsync(int take, string adminUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, forUpdate: false, cancellationToken);
        if (take is < 1 or > 100)
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        var runs = await domainUnitOfWork.ActivityEnforcementJobRuns.ListRecentAsync(take, cancellationToken);
        return runs.Select(run => new ActivityJobRunDto(
            run.Id,
            run.JobType,
            run.TargetScoreDateEgypt,
            run.TargetWeekStartDateEgypt,
            run.Status,
            run.ProcessedCount,
            run.SkippedCount,
            run.CreatedCount,
            run.UpdatedCount,
            run.FailedCount,
            run.SafeFailureSummary)).ToArray();
    }

    public async Task<ActivityJobRunDto> RunDailyScoreAsync(RunDailyActivityScoreRequestDto request, string adminUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, forUpdate: false, cancellationToken);
        var validation = await dailyScoreValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        return await activityScoreService.RunDailyScoreAsync(request.ScoreDateEgypt, adminUserId, cancellationToken);
    }

    public async Task<ActivityJobRunDto> RunWeeklyEnforcementAsync(RunWeeklyEnforcementRequestDto request, string adminUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, forUpdate: false, cancellationToken);
        var validation = await weeklyEnforcementValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        return await weeklyEnforcementService.RunWeeklyEnforcementAsync(request.WeekStartDateEgypt, adminUserId, cancellationToken);
    }

    public async Task<ActivityJobRunDto> RunSuspensionExpiryAsync(string adminUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, forUpdate: false, cancellationToken);
        return await activityScoreService.ExpireSuspensionsAsync(adminUserId, cancellationToken);
    }

    private ViolationSummaryCriteria NormalizeViolationCriteria(ViolationSummaryQuery query)
    {
        if (query.PageNumber < 1 || query.PageSize is < 1 or > 100 || query.MinRollingViolations is < 0)
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        if (!string.IsNullOrWhiteSpace(query.Eligibility)
            && !string.Equals(query.Eligibility, "Warning", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(query.Eligibility, "ActionEligible", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(query.Eligibility, "None", StringComparison.OrdinalIgnoreCase))
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        if ((query.WeekFrom.HasValue && query.WeekFrom.Value.DayOfWeek != DayOfWeek.Monday)
            || (query.WeekTo.HasValue && query.WeekTo.Value.DayOfWeek != DayOfWeek.Monday)
            || (query.WeekFrom.HasValue && query.WeekTo.HasValue && query.WeekFrom.Value > query.WeekTo.Value))
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        var weekTo = query.WeekTo ?? GetLastCompletedWeekStartEgypt();
        var weekFrom = query.WeekFrom ?? weekTo.AddDays(-49);
        if (weekFrom > weekTo)
        {
            throw new Phase9ValidationException("Validation failed.");
        }

        return new ViolationSummaryCriteria(
            query.DoctorId,
            query.Status,
            query.Eligibility?.Trim(),
            weekFrom,
            weekTo,
            query.MinRollingViolations);
    }

    private DateOnly GetLastCompletedWeekStartEgypt()
    {
        var businessDate = businessClock.Capture().BusinessDateEgypt;
        var daysSinceMonday = ((int)businessDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return businessDate.AddDays(-daysSinceMonday - 7);
    }

    private async Task<Core.Entities.Identity.ApplicationUser> EnsureAdminAsync(string adminUserId, bool forUpdate, CancellationToken cancellationToken)
    {
        var admin = forUpdate
            ? await identityUnitOfWork.Users.FindByIdForUpdateAsync(adminUserId, cancellationToken)
            : await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            throw new Phase9ForbiddenException("Forbidden.");
        }

        return admin;
    }
}
