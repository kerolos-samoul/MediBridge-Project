using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IDeliveryRepository
{
    Task AddDeliveryAsync(string deliveryId, string doctorId, string campaignId, string companyId, DateOnly deliveryDateEgypt, CancellationToken cancellationToken = default);
    Task<string?> FindDeliveryIdAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default);
    Task<bool> DeliveryExistsAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListDeliveryHistoryIdsAsync(string doctorId, DateOnly? fromDateEgypt = null, DateOnly? toDateEgypt = null, DeliveryStatus? status = null, CancellationToken cancellationToken = default);
}
