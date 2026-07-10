using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class DeliveryInteractionOperationRepository : IDeliveryInteractionOperationRepository
{
    private readonly MediBridgeDbContext context;

    public DeliveryInteractionOperationRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public Task<DeliveryInteractionOperation?> FindForUpdateAsync(
        string doctorId,
        string deliveryId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return context.DeliveryInteractionOperations
            .FromSqlInterpolated($"""
                SELECT *
                FROM [DeliveryInteractionOperations] WITH (UPDLOCK, ROWLOCK)
                WHERE [DoctorId] = {doctorId}
                    AND [DeliveryId] = {deliveryId}
                    AND [IdempotencyKey] = {idempotencyKey}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(DeliveryInteractionOperation operation, CancellationToken cancellationToken = default)
    {
        await context.DeliveryInteractionOperations.AddAsync(operation, cancellationToken);
    }

    public async Task MarkSucceededAsync(
        string operationId,
        string chargeTransactionId,
        string earnTransactionId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var operation = await context.DeliveryInteractionOperations
            .FirstOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken)
            ?? throw new InvalidOperationException($"Interaction operation '{operationId}' was not found.");

        operation.MarkSucceeded(chargeTransactionId, earnTransactionId, completedAtUtc);
    }

    public Task<DeliveryInteractionReplayEvidenceReadModel?> FindSucceededForDeliveryAsync(
        string doctorId,
        string deliveryId,
        DeliveryInteractionDecision decision,
        CancellationToken cancellationToken = default)
    {
        return context.DeliveryInteractionOperations
            .AsNoTracking()
            .Where(operation => operation.DoctorId == doctorId
                && operation.DeliveryId == deliveryId
                && operation.Decision == decision
                && operation.Status == DeliveryInteractionOperationStatus.Succeeded)
            .OrderBy(operation => operation.CompletedAtUtc)
            .Select(operation => new DeliveryInteractionReplayEvidenceReadModel(
                operation.Id,
                operation.DeliveryId,
                operation.Decision,
                operation.FeedbackText,
                operation.Status,
                operation.ChargeTransactionId,
                operation.EarnTransactionId,
                operation.CreatedAtUtc,
                operation.CompletedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
