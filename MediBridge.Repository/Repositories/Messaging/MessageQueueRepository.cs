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

    public async Task AddQueueItemsAsync(IEnumerable<DoctorMessageQueue> queueItems, CancellationToken cancellationToken = default)
    {
        await context.DoctorMessageQueues.AddRangeAsync(queueItems, cancellationToken);
    }

    public async Task<bool> TryAddQueueItemAsync(DoctorMessageQueue queueItem, CancellationToken cancellationToken = default)
    {
        var status = (int)queueItem.Status;
        var rowsAffected = await context.Database.ExecuteSqlInterpolatedAsync($"""
            IF NOT EXISTS (
                SELECT 1
                FROM [DoctorMessageQueues] WITH (UPDLOCK, HOLDLOCK)
                WHERE [CampaignId] = {queueItem.CampaignId}
                    AND [DoctorId] = {queueItem.DoctorId}
            )
            BEGIN
                INSERT INTO [DoctorMessageQueues]
                    ([Id], [DoctorId], [CampaignId], [QueuedAtUtc], [CampaignSubmittedAtUtc], [Status], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES
                    ({queueItem.Id}, {queueItem.DoctorId}, {queueItem.CampaignId}, {queueItem.QueuedAtUtc}, {queueItem.CampaignSubmittedAtUtc}, {status}, {queueItem.CreatedAtUtc}, {queueItem.UpdatedAtUtc})
            END
            """, cancellationToken);

        return rowsAffected > 0;
    }

    public Task<bool> QueueItemExistsAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default)
    {
        return context.DoctorMessageQueues.AnyAsync(queue => queue.CampaignId == campaignId && queue.DoctorId == doctorId, cancellationToken);
    }

    public Task<DoctorMessageQueue?> FindQueueItemAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default)
    {
        return context.DoctorMessageQueues.FirstOrDefaultAsync(
            queue => queue.CampaignId == campaignId && queue.DoctorId == doctorId,
            cancellationToken);
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

    public async Task<IReadOnlyList<DoctorMessageQueue>> ListQueueItemsForDoctorAsync(string doctorId, QueueItemStatus status, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await context.DoctorMessageQueues
            .Where(queue => queue.DoctorId == doctorId && queue.Status == status)
            .OrderBy(queue => queue.QueuedAtUtc)
            .ThenBy(queue => queue.Id)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Max(take, 1))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<QueueItemStatus, int>> CountQueueItemsByCampaignAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        var counts = await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.CampaignId == campaignId)
            .GroupBy(queue => queue.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(item => item.Status, item => item.Count);
    }

    public async Task UpdateQueueItemStatusAsync(string queueItemId, QueueItemStatus status, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        var queueItem = await context.DoctorMessageQueues.FirstOrDefaultAsync(queue => queue.Id == queueItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Queue item '{queueItemId}' was not found.");

        queueItem.Status = status;
        queueItem.UpdatedAtUtc = updatedAtUtc;
    }
}
