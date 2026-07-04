using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace MediBridge.Services.Services;

public sealed class DeliveryJobRecoveryCoordinator : IDeliveryJobRecoveryCoordinator
{
    private static readonly TimeSpan InjectorEligibilityTime = new(0, 5, 0);
    private const string SafeSchedulerFailure = "The delivery scheduler could not acknowledge recovery work.";
    private readonly IDomainUnitOfWork unitOfWork;
    private readonly IEgyptBusinessClock clock;
    private readonly IDeliveryJobEnqueuer enqueuer;
    private readonly ILogger<DeliveryJobRecoveryCoordinator> logger;

    public DeliveryJobRecoveryCoordinator(
        IDomainUnitOfWork unitOfWork,
        IEgyptBusinessClock clock,
        IDeliveryJobEnqueuer enqueuer,
        ILogger<DeliveryJobRecoveryCoordinator> logger)
    {
        this.unitOfWork = unitOfWork;
        this.clock = clock;
        this.enqueuer = enqueuer;
        this.logger = logger;
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = clock.Capture();
        var injectorEligible = snapshot.EgyptLocalNow.TimeOfDay >= InjectorEligibilityTime;

        await EnsureClaimsAsync(snapshot.BusinessDateEgypt, snapshot.UtcNow, injectorEligible, cancellationToken);
        try
        {
            await DispatchClaimsAsync(snapshot.BusinessDateEgypt, snapshot.UtcNow, injectorEligible, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await MarkPendingClaimsFailedAsync(snapshot.BusinessDateEgypt, snapshot.UtcNow, cancellationToken);
            logger.LogError(
                "Delivery recovery coordination failed safely for {BusinessDateEgypt}; pending work will reconcile on restart.",
                snapshot.BusinessDateEgypt);
            throw;
        }
    }

    private Task EnsureClaimsAsync(
        DateOnly businessDate,
        DateTime claimedAtUtc,
        bool injectorEligible,
        CancellationToken cancellationToken)
    {
        return unitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            if (!await unitOfWork.DeliveryJobRuns.HasCurrentDateCoverageAsync(
                    DeliveryJobType.ExpiryCleaner, businessDate, transactionCancellationToken))
            {
                await unitOfWork.DeliveryRecoveryDispatches.FindOrCreateClaimAsync(
                    businessDate, DeliveryJobType.ExpiryCleaner, claimedAtUtc, transactionCancellationToken);
            }

            if (injectorEligible && !await unitOfWork.DeliveryJobRuns.HasCurrentDateCoverageAsync(
                    DeliveryJobType.DailyInjector, businessDate, transactionCancellationToken))
            {
                await unitOfWork.DeliveryRecoveryDispatches.FindOrCreateClaimAsync(
                    businessDate, DeliveryJobType.DailyInjector, claimedAtUtc, transactionCancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    private Task DispatchClaimsAsync(
        DateOnly businessDate,
        DateTime enqueuedAtUtc,
        bool injectorEligible,
        CancellationToken cancellationToken)
    {
        return unitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var expiryCovered = await unitOfWork.DeliveryJobRuns.HasCurrentDateCoverageAsync(
                DeliveryJobType.ExpiryCleaner, businessDate, transactionCancellationToken);
            DeliveryRecoveryDispatch? expiry = null;
            string? expirySchedulerJobId = null;

            if (!expiryCovered)
            {
                expiry = await unitOfWork.DeliveryRecoveryDispatches.FindOrCreateClaimAsync(
                    businessDate, DeliveryJobType.ExpiryCleaner, enqueuedAtUtc, transactionCancellationToken);
                var expiryStatus = expiry.Status;
                if (expiryStatus == RecoveryDispatchStatus.Failed)
                {
                    await unitOfWork.DeliveryRecoveryDispatches.ResetForRetryAsync(
                        expiry.Id, enqueuedAtUtc, transactionCancellationToken);
                    expiryStatus = RecoveryDispatchStatus.Pending;
                }

                if (expiryStatus == RecoveryDispatchStatus.Pending)
                {
                    var result = await enqueuer.EnqueueExpiryAsync(transactionCancellationToken);
                    if (!await unitOfWork.DeliveryRecoveryDispatches.RecordEnqueuedAsync(
                            expiry.Id, result.SchedulerJobId, null, enqueuedAtUtc, transactionCancellationToken))
                    {
                        throw new InvalidOperationException("The expiry scheduler acknowledgement conflicted.");
                    }

                    expirySchedulerJobId = result.SchedulerJobId;
                    logger.LogInformation(
                        "Recovery dispatch {DispatchId} enqueued {JobType} for {BusinessDateEgypt} as scheduler job {SchedulerJobId}.",
                        expiry.Id, DeliveryJobType.ExpiryCleaner, businessDate, result.SchedulerJobId);
                }
                else if (expiryStatus == RecoveryDispatchStatus.Enqueued)
                {
                    expirySchedulerJobId = expiry.SchedulerJobId;
                }
                else if (expiryStatus == RecoveryDispatchStatus.Completed)
                {
                    expiryCovered = true;
                }
            }

            if (injectorEligible && !await unitOfWork.DeliveryJobRuns.HasCurrentDateCoverageAsync(
                    DeliveryJobType.DailyInjector, businessDate, transactionCancellationToken))
            {
                var injector = await unitOfWork.DeliveryRecoveryDispatches.FindOrCreateClaimAsync(
                    businessDate, DeliveryJobType.DailyInjector, enqueuedAtUtc, transactionCancellationToken);
                var injectorStatus = injector.Status;
                if (injectorStatus == RecoveryDispatchStatus.Failed)
                {
                    await unitOfWork.DeliveryRecoveryDispatches.ResetForRetryAsync(
                        injector.Id, enqueuedAtUtc, transactionCancellationToken);
                    injectorStatus = RecoveryDispatchStatus.Pending;
                }

                if (injectorStatus == RecoveryDispatchStatus.Pending)
                {
                    DeliveryJobEnqueueResult result;
                    string? dependency = null;
                    if (expiryCovered)
                    {
                        result = await enqueuer.EnqueueInjectorAsync(transactionCancellationToken);
                    }
                    else if (!string.IsNullOrWhiteSpace(expirySchedulerJobId) && expiry is not null)
                    {
                        result = await enqueuer.EnqueueInjectorContinuationAsync(
                            expirySchedulerJobId, transactionCancellationToken);
                        dependency = expiry.Id;
                    }
                    else
                    {
                        throw new InvalidOperationException("Expiry recovery must be persisted before injector recovery.");
                    }

                    if (!await unitOfWork.DeliveryRecoveryDispatches.RecordEnqueuedAsync(
                            injector.Id, result.SchedulerJobId, dependency, enqueuedAtUtc, transactionCancellationToken))
                    {
                        throw new InvalidOperationException("The injector scheduler acknowledgement conflicted.");
                    }

                    logger.LogInformation(
                        "Recovery dispatch {DispatchId} enqueued {JobType} for {BusinessDateEgypt} with dependency {DependsOnDispatchId} as scheduler job {SchedulerJobId}.",
                        injector.Id, DeliveryJobType.DailyInjector, businessDate, dependency, result.SchedulerJobId);
                }
            }

            return true;
        }, cancellationToken);
    }

    private async Task MarkPendingClaimsFailedAsync(
        DateOnly businessDate,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var claims = await unitOfWork.DeliveryRecoveryDispatches.ListPendingClaimsAsync(
                businessDate, transactionCancellationToken);
            foreach (var claim in claims)
            {
                await unitOfWork.DeliveryRecoveryDispatches.CompleteAsync(
                    claim.Id,
                    RecoveryDispatchStatus.Failed,
                    failedAtUtc,
                    SafeSchedulerFailure,
                    transactionCancellationToken);
            }

            return true;
        }, cancellationToken);
    }
}
