using MediBridge.Core.Entities.Messaging;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IDeliveryInteractionRepository
{
    Task<DeliveryInteraction?> FindByIdempotencyHashAsync(string doctorId, string idempotencyKeyHash, CancellationToken cancellationToken = default);
    Task<DeliveryInteraction?> FindByDeliveryIdAsync(string deliveryId, CancellationToken cancellationToken = default);
    Task AddInteractionAsync(DeliveryInteraction interaction, CancellationToken cancellationToken = default);
}
