using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IMessageQueueRepository
{
    Task AddQueueItemAsync(string queueItemId, string doctorId, string campaignId, DateTime queuedAtUtc, CancellationToken cancellationToken = default);

    // QueuedAtUtc is derived from campaign submission time or queue insertion time. Phase 3 has no priority queue behavior.
    Task<IReadOnlyList<string>> ListActiveQueueItemIdsForDoctorAsync(string doctorId, QueueItemStatus status, CancellationToken cancellationToken = default);

    Task UpdateQueueItemStatusAsync(string queueItemId, QueueItemStatus status, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
}
