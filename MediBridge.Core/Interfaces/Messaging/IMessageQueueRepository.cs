using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IMessageQueueRepository
{
    Task AddQueueItemAsync(string queueItemId, string doctorId, string campaignId, DateTime campaignSubmittedAtUtc, DateTime queuedAtUtc, CancellationToken cancellationToken = default);
    Task AddQueueItemsAsync(IEnumerable<DoctorMessageQueue> queueItems, CancellationToken cancellationToken = default);
    Task<bool> TryAddQueueItemAsync(DoctorMessageQueue queueItem, CancellationToken cancellationToken = default);
    Task<bool> QueueItemExistsAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default);
    Task<DoctorMessageQueue?> FindQueueItemAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default);

    // Cursor is exclusive. Equal submission timestamps are disambiguated by Id, so a page boundary cannot skip or duplicate rows.
    Task<IReadOnlyList<QueuedDoctorReadModel>> ListQueuedDoctorsPageAsync(QueuedDoctorCursor? after, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryQueueCandidateReadModel>> ListQueuedCandidatesPageAsync(string doctorId, QueuedCandidateCursor? after, int take, CancellationToken cancellationToken = default);
    Task<DoctorMessageQueue?> FindQueuedItemForUpdateAsync(string queueItemId, CancellationToken cancellationToken = default);
    Task<bool> TryMarkActivatedAsync(string queueItemId, DateTime activatedAtUtc, CancellationToken cancellationToken = default);
    Task<bool> TryMarkCancelledAsync(string queueItemId, DateTime cancelledAtUtc, CancellationToken cancellationToken = default);

    // Legacy list methods retain immutable campaign-submission FIFO semantics.
    Task<IReadOnlyList<string>> ListActiveQueueItemIdsForDoctorAsync(string doctorId, QueueItemStatus status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorMessageQueue>> ListQueueItemsForDoctorAsync(string doctorId, QueueItemStatus status, int skip, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<QueueItemStatus, int>> CountQueueItemsByCampaignAsync(string campaignId, CancellationToken cancellationToken = default);

    Task UpdateQueueItemStatusAsync(string queueItemId, QueueItemStatus status, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
}
