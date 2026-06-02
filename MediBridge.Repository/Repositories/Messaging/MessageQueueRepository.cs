using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class MessageQueueRepository : IMessageQueueRepository
{
    private readonly MediBridgeDbContext context;

    public MessageQueueRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddQueueItemAsync(string queueItemId, string doctorId, string campaignId, DateTime queuedAtUtc, CancellationToken cancellationToken = default)
    {
        await context.DoctorMessageQueues.AddAsync(new DoctorMessageQueue
        {
            Id = queueItemId,
            DoctorId = doctorId,
            CampaignId = campaignId,
            QueuedAtUtc = queuedAtUtc,
            Status = QueueItemStatus.Queued
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActiveQueueItemIdsForDoctorAsync(string doctorId, QueueItemStatus status, CancellationToken cancellationToken = default)
    {
        return await context.DoctorMessageQueues
            .Where(queue => queue.DoctorId == doctorId && queue.Status == status)
            .OrderBy(queue => queue.QueuedAtUtc)
            .ThenBy(queue => queue.Id)
            .Select(queue => queue.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateQueueItemStatusAsync(string queueItemId, QueueItemStatus status, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        var queueItem = await context.DoctorMessageQueues.FirstOrDefaultAsync(queue => queue.Id == queueItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Queue item '{queueItemId}' was not found.");

        queueItem.Status = status;
        queueItem.UpdatedAtUtc = updatedAtUtc;
    }
}
