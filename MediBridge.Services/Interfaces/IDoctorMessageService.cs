using MediBridge.Services.DTOs.Messaging;

namespace MediBridge.Services.Interfaces;

public interface IDoctorMessageService
{
    Task<TodayInboxDto> GetTodayInboxAsync(
        string actorUserId,
        int? pageSize = null,
        string? cursor = null,
        CancellationToken cancellationToken = default);

    Task<DeliveryAssetAccessGrantDto> CreateDeliveryAssetAccessGrantAsync(
        string actorUserId,
        string deliveryId,
        string fileId,
        CancellationToken cancellationToken = default);
}
