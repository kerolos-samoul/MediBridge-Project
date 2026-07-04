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

    public async Task AddQueueItemAsync(string queueItemId, string doctorId, string campaignId, DateTime campaignSubmittedAtUtc, DateTime queuedAtUtc, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticSubmissionTimestamp(campaignSubmittedAtUtc);
        await context.DoctorMessageQueues.AddAsync(new DoctorMessageQueue
        {
            Id = queueItemId,
            DoctorId = doctorId,
            CampaignId = campaignId,
            QueuedAtUtc = queuedAtUtc,
            CampaignSubmittedAtUtc = campaignSubmittedAtUtc,
            Status = QueueItemStatus.Queued
        }, cancellationToken);
    }

    public async Task AddQueueItemsAsync(IEnumerable<DoctorMessageQueue> queueItems, CancellationToken cancellationToken = default)
    {
        var materialized = queueItems.ToArray();
        foreach (var queueItem in materialized.Where(item => item.Status == QueueItemStatus.Queued))
        {
            EnsureAuthenticSubmissionTimestamp(queueItem.CampaignSubmittedAtUtc);
        }

        await context.DoctorMessageQueues.AddRangeAsync(materialized, cancellationToken);
    }

    public async Task<bool> TryAddQueueItemAsync(DoctorMessageQueue queueItem, CancellationToken cancellationToken = default)
    {
        if (queueItem.Status != QueueItemStatus.Queued)
        {
            throw new ArgumentException("Only Queued rows can be created through the queue insertion contract.", nameof(queueItem));
        }

        EnsureAuthenticSubmissionTimestamp(queueItem.CampaignSubmittedAtUtc);
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
        if (status == QueueItemStatus.Queued)
        {
            await ThrowIfMalformedQueuedRowsExistAsync(doctorId, cancellationToken);
        }

        return await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.DoctorId == doctorId && queue.Status == status)
            .OrderBy(queue => queue.CampaignSubmittedAtUtc)
            .ThenBy(queue => queue.Id)
            .Select(queue => queue.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorMessageQueue>> ListQueueItemsForDoctorAsync(string doctorId, QueueItemStatus status, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.DoctorId == doctorId && queue.Status == status)
            .OrderBy(queue => queue.CampaignSubmittedAtUtc)
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

        switch (status)
        {
            case QueueItemStatus.Activated:
                queueItem.MarkActivated(updatedAtUtc);
                break;
            case QueueItemStatus.Cancelled:
                queueItem.MarkCancelled(updatedAtUtc);
                break;
            default:
                throw new InvalidOperationException("Queued is not a valid terminal transition target.");
        }
    }

    public async Task<IReadOnlyList<QueuedDoctorReadModel>> ListQueuedDoctorsPageAsync(
        QueuedDoctorCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var boundedTake = EnsureBoundedTake(take);
        var query = context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.Status == QueueItemStatus.Queued);
        if (after is not null)
        {
            query = query.Where(queue => string.Compare(queue.DoctorId, after.DoctorId) > 0);
        }

        return await query
            .Select(queue => queue.DoctorId)
            .Distinct()
            .OrderBy(doctorId => doctorId)
            .Take(boundedTake)
            .Select(doctorId => new QueuedDoctorReadModel(doctorId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryQueueCandidateReadModel>> ListQueuedCandidatesPageAsync(
        string doctorId,
        QueuedCandidateCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var boundedTake = EnsureBoundedTake(take);
        var query = context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.DoctorId == doctorId
                && queue.Status == QueueItemStatus.Queued);
        if (after is not null)
        {
            if (after.CampaignSubmittedAtUtc is null)
            {
                query = query.Where(queue => queue.CampaignSubmittedAtUtc != null
                    || queue.CampaignSubmittedAtUtc == null && string.Compare(queue.Id, after.Id) > 0);
            }
            else
            {
                query = query.Where(queue => queue.CampaignSubmittedAtUtc > after.CampaignSubmittedAtUtc
                    || queue.CampaignSubmittedAtUtc == after.CampaignSubmittedAtUtc && string.Compare(queue.Id, after.Id) > 0);
            }
        }

        return await query
            .OrderBy(queue => queue.CampaignSubmittedAtUtc)
            .ThenBy(queue => queue.Id)
            .Take(boundedTake)
            .Select(queue => new DeliveryQueueCandidateReadModel(
                queue.Id,
                queue.DoctorId,
                queue.CampaignId,
                queue.CampaignSubmittedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<DoctorMessageQueue?> FindQueuedItemForUpdateAsync(string queueItemId, CancellationToken cancellationToken = default)
    {
        return context.DoctorMessageQueues
            .FromSqlInterpolated($"SELECT * FROM [DoctorMessageQueues] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {queueItemId} AND [Status] = {(int)QueueItemStatus.Queued}")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryMarkActivatedAsync(string queueItemId, DateTime activatedAtUtc, CancellationToken cancellationToken = default)
    {
        var queueItem = await FindQueuedItemForUpdateAsync(queueItemId, cancellationToken);
        if (queueItem is null)
        {
            return false;
        }

        queueItem.MarkActivated(activatedAtUtc);
        return true;
    }

    public async Task<bool> TryMarkCancelledAsync(string queueItemId, DateTime cancelledAtUtc, CancellationToken cancellationToken = default)
    {
        var queueItem = await FindQueuedItemForUpdateAsync(queueItemId, cancellationToken);
        if (queueItem is null)
        {
            return false;
        }

        queueItem.MarkCancelled(cancelledAtUtc);
        return true;
    }

    private async Task ThrowIfMalformedQueuedRowsExistAsync(string doctorId, CancellationToken cancellationToken)
    {
        if (await context.DoctorMessageQueues.AsNoTracking()
            .AnyAsync(queue => queue.DoctorId == doctorId
                && queue.Status == QueueItemStatus.Queued
                && queue.CampaignSubmittedAtUtc == null,
                cancellationToken))
        {
            throw new InvalidOperationException("A Queued row is missing its authentic campaign submission timestamp.");
        }
    }

    private static int EnsureBoundedTake(int take)
    {
        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "Page size must be between 1 and 1000.");
        }

        return take;
    }

    private static void EnsureAuthenticSubmissionTimestamp(DateTime? campaignSubmittedAtUtc)
    {
        if (campaignSubmittedAtUtc is null || campaignSubmittedAtUtc.Value.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("Queued rows require an authentic non-local campaign submission timestamp.", nameof(campaignSubmittedAtUtc));
        }
    }
}
