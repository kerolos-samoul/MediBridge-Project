using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IMessageQueueRepository
{
    Task AddQueueItemAsync(string queueItemId, string doctorId, string campaignId, DateTime queuedAtUtc, CancellationToken cancellationToken = default);
    Task AddQueueItemAsync(DoctorMessageQueue queueItem, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorMessageQueue>> ListQueueItemsByCampaignAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<QueueItemStatus, int>> GetQueueItemCountsByCampaignAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<bool> ActiveQueueItemExistsAsync(string campaignId, string doctorId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> ListActiveQueueDoctorIdsAsync(string campaignId, IReadOnlyCollection<string> doctorIds, CancellationToken cancellationToken = default);

    // Active doctor rows are ordered by submitted campaign time, queue creation time, and stable row id.
    Task<IReadOnlyList<string>> ListActiveQueueItemIdsForDoctorAsync(string doctorId, QueueItemStatus status, CancellationToken cancellationToken = default);

    Task UpdateQueueItemStatusAsync(string queueItemId, QueueItemStatus status, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
}
