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

    Task<ReadTrackingResultDto> MarkReadAsync(
        string actorUserId,
        string deliveryId,
        CancellationToken cancellationToken = default);

    Task<DoctorInteractionResultDto> InteractAsync(
        string actorUserId,
        string deliveryId,
        string? idempotencyKey,
        DoctorInteractionRequestDto request,
        CancellationToken cancellationToken = default);
}
