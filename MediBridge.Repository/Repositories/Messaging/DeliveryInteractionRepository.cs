using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class DeliveryInteractionRepository : IDeliveryInteractionRepository
{
    private readonly MediBridgeDbContext context;

    public DeliveryInteractionRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public Task<DeliveryInteraction?> FindByIdempotencyHashAsync(
        string doctorId,
        string idempotencyKeyHash,
        CancellationToken cancellationToken = default)
    {
        return context.DeliveryInteractions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                interaction => interaction.DoctorId == doctorId
                    && interaction.IdempotencyKeyHash == idempotencyKeyHash,
                cancellationToken);
    }

    public Task<DeliveryInteraction?> FindByDeliveryIdAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        return context.DeliveryInteractions
            .AsNoTracking()
            .FirstOrDefaultAsync(interaction => interaction.DeliveryId == deliveryId, cancellationToken);
    }

    public async Task AddInteractionAsync(DeliveryInteraction interaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(interaction.IdempotencyKeyHash))
        {
            throw new ArgumentException("A normalized idempotency hash is required.", nameof(interaction));
        }

        await context.DeliveryInteractions.AddAsync(interaction, cancellationToken);
    }
}
