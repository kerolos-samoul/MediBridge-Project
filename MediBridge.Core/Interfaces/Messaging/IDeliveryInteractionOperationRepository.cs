using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IDeliveryInteractionOperationRepository
{
    Task<DeliveryInteractionOperation?> FindForUpdateAsync(
        string doctorId,
        string deliveryId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task AddAsync(DeliveryInteractionOperation operation, CancellationToken cancellationToken = default);

    Task MarkSucceededAsync(
        string operationId,
        string chargeTransactionId,
        string earnTransactionId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default);

    Task<DeliveryInteractionReplayEvidenceReadModel?> FindSucceededForDeliveryAsync(
        string doctorId,
        string deliveryId,
        DeliveryInteractionDecision decision,
        CancellationToken cancellationToken = default);
}
