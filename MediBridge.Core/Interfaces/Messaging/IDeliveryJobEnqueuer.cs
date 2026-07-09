namespace MediBridge.Core.Interfaces.Messaging;

public sealed record DeliveryJobEnqueueResult(string SchedulerJobId);

public interface IDeliveryJobEnqueuer
{
    Task<DeliveryJobEnqueueResult> EnqueueExpiryAsync(CancellationToken cancellationToken = default);
    Task<DeliveryJobEnqueueResult> EnqueueInjectorContinuationAsync(string expirySchedulerJobId, CancellationToken cancellationToken = default);
    Task<DeliveryJobEnqueueResult> EnqueueInjectorAsync(CancellationToken cancellationToken = default);
}
