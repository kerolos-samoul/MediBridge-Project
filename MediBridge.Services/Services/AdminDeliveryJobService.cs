using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class AdminDeliveryJobService : IAdminDeliveryJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IDeliveryJobEnqueuer enqueuer;
    private readonly IEgyptBusinessClock businessClock;

    public AdminDeliveryJobService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IDeliveryJobEnqueuer enqueuer,
        IEgyptBusinessClock businessClock)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.enqueuer = enqueuer;
        this.businessClock = businessClock;
    }

    public async Task<DeliveryJobStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = businessClock.Capture();
        var runs = await domainUnitOfWork.DeliveryJobRuns.ListRecentAsync(20, cancellationToken);
        var dispatches = await domainUnitOfWork.DeliveryRecoveryDispatches.ListRecentAsync(20, cancellationToken);
        return new DeliveryJobStatusDto(
            snapshot.BusinessDateEgypt,
            runs.Select(ToDto).ToArray(),
            dispatches.Select(ToDto).ToArray());
    }

    public Task<DeliveryJobEnqueueDto> EnqueueExpiryAsync(
        string adminUserId,
        RunDeliveryJobRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(
            adminUserId,
            request,
            "ManualDeliveryExpiryEnqueued",
            "ExpiryCleaner",
            () => enqueuer.EnqueueExpiryAsync(cancellationToken),
            cancellationToken);
    }

    public Task<DeliveryJobEnqueueDto> EnqueueInjectorAsync(
        string adminUserId,
        RunDeliveryJobRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(
            adminUserId,
            request,
            "ManualDailyInjectorEnqueued",
            "DailyInjector",
            () => enqueuer.EnqueueInjectorAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<DeliveryJobEnqueueDto> EnqueueAsync(
        string adminUserId,
        RunDeliveryJobRequestDto request,
        string auditEventType,
        string jobType,
        Func<Task<DeliveryJobEnqueueResult>> operation,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new Phase5ValidationException("Validation failed.", ["A reason is required for manual delivery job execution."]);
        }

        var admin = await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var snapshot = businessClock.Capture();
        var enqueuedAtUtc = snapshot.UtcNow;
        var result = await operation();
        await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            Guid.NewGuid().ToString("N"),
            auditEventType,
            admin.Id,
            admin.Role.ToString(),
            AuditTargetType.AuditEvent,
            result.SchedulerJobId,
            AuditOutcome.Success,
            request.Reason.Trim(),
            correlationId: null,
            JsonSerializer.Serialize(new
            {
                JobType = jobType,
                result.SchedulerJobId,
                snapshot.BusinessDateEgypt
            }, JsonOptions),
            enqueuedAtUtc,
            cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);

        return new DeliveryJobEnqueueDto(jobType, result.SchedulerJobId, snapshot.BusinessDateEgypt, enqueuedAtUtc);
    }

    private static DeliveryJobRunSummaryDto ToDto(Core.Entities.Messaging.DeliveryJobRun run)
    {
        return new DeliveryJobRunSummaryDto(
            run.Id,
            run.JobType,
            run.BusinessDateEgypt,
            run.Status,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.ExaminedCount,
            run.ActivatedCount,
            run.ExpiredCount,
            run.CancelledCount,
            run.SkippedCount,
            run.FailedCount,
            run.SafeFailureSummary);
    }

    private static DeliveryRecoveryDispatchSummaryDto ToDto(Core.Entities.Messaging.DeliveryRecoveryDispatch dispatch)
    {
        return new DeliveryRecoveryDispatchSummaryDto(
            dispatch.Id,
            dispatch.BusinessDateEgypt,
            dispatch.JobType,
            dispatch.Status,
            dispatch.SchedulerJobId,
            dispatch.DependsOnDispatchId,
            dispatch.ClaimedAtUtc,
            dispatch.EnqueuedAtUtc,
            dispatch.CompletedAtUtc,
            dispatch.SafeFailureSummary);
    }
}
