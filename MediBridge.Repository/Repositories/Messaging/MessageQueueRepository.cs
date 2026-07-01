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

    public async Task AddQueueItemAsync(DoctorMessageQueue queueItem, CancellationToken cancellationToken = default)
    {
        await context.DoctorMessageQueues.AddAsync(queueItem, cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorMessageQueue>> ListQueueItemsByCampaignAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.CampaignId == campaignId)
            .OrderBy(queue => queue.CampaignSubmittedAtUtc)
            .ThenBy(queue => queue.QueuedAtUtc)
            .ThenBy(queue => queue.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<QueueItemStatus, int>> GetQueueItemCountsByCampaignAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.CampaignId == campaignId)
            .GroupBy(queue => queue.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
    }

    public Task<bool> ActiveQueueItemExistsAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default)
    {
        return context.DoctorMessageQueues.AnyAsync(
            queue => queue.CampaignId == campaignId
                && queue.DoctorId == doctorId
                && (queue.Status == QueueItemStatus.Queued || queue.Status == QueueItemStatus.Activated),
            cancellationToken);
    }

    public async Task<IReadOnlySet<string>> ListActiveQueueDoctorIdsAsync(
        string campaignId,
        IReadOnlyCollection<string> doctorIds,
        CancellationToken cancellationToken = default)
    {
        if (doctorIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var requestedDoctorIds = doctorIds.ToArray();
        var activeDoctorIds = await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.CampaignId == campaignId
                && requestedDoctorIds.Contains(queue.DoctorId)
                && (queue.Status == QueueItemStatus.Queued || queue.Status == QueueItemStatus.Activated))
            .Select(queue => queue.DoctorId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return activeDoctorIds.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> ListActiveQueueItemIdsForDoctorAsync(string doctorId, QueueItemStatus status, CancellationToken cancellationToken = default)
    {
        return await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.DoctorId == doctorId && queue.Status == status)
            .OrderBy(queue => queue.CampaignSubmittedAtUtc)
            .ThenBy(queue => queue.QueuedAtUtc)
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
