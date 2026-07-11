using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IDeliveryRecoveryDispatchRepository
{
    Task<DeliveryRecoveryDispatch> FindOrCreateClaimAsync(DateOnly businessDateEgypt, DeliveryJobType jobType, DateTime claimedAtUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryRecoveryDispatch>> ListPendingClaimsAsync(DateOnly businessDateEgypt, CancellationToken cancellationToken = default);
    Task<bool> ResetForRetryAsync(string dispatchId, DateTime claimedAtUtc, CancellationToken cancellationToken = default);
    Task<bool> RecordEnqueuedAsync(string dispatchId, string schedulerJobId, string? dependsOnDispatchId, DateTime enqueuedAtUtc, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(string dispatchId, RecoveryDispatchStatus status, DateTime completedAtUtc, string? safeFailureSummary, CancellationToken cancellationToken = default);
    Task<bool> CompleteEnqueuedForJobAsync(DateOnly businessDateEgypt, DeliveryJobType jobType, DateTime completedAtUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryRecoveryDispatch>> ListRecentAsync(int take, CancellationToken cancellationToken = default);
}
