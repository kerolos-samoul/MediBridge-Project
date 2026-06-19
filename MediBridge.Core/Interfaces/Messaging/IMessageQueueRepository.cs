using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IMessageQueueRepository
{
    Task AddQueueItemAsync(string queueItemId, string doctorId, string campaignId, DateTime queuedAtUtc, CancellationToken cancellationToken = default);
    Task AddQueueItemsAsync(IEnumerable<DoctorMessageQueue> queueItems, CancellationToken cancellationToken = default);
    Task<bool> TryAddQueueItemAsync(DoctorMessageQueue queueItem, CancellationToken cancellationToken = default);
    Task<bool> QueueItemExistsAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default);
    Task<DoctorMessageQueue?> FindQueueItemAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default);

    // QueuedAtUtc is derived from campaign submission time or queue insertion time. Phase 3 has no priority queue behavior.
    Task<IReadOnlyList<string>> ListActiveQueueItemIdsForDoctorAsync(string doctorId, QueueItemStatus status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorMessageQueue>> ListQueueItemsForDoctorAsync(string doctorId, QueueItemStatus status, int skip, int take, CancellationToken cancellationToken = default);

    Task UpdateQueueItemStatusAsync(string queueItemId, QueueItemStatus status, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
}
