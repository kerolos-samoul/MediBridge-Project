using MediBridge.Services.DTOs.Messaging;

namespace MediBridge.Services.Interfaces;

public interface IAdminDeliveryJobService
{
    Task<DeliveryJobStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<DeliveryJobEnqueueDto> EnqueueExpiryAsync(
        string adminUserId,
        RunDeliveryJobRequestDto request,
        CancellationToken cancellationToken = default);

    Task<DeliveryJobEnqueueDto> EnqueueInjectorAsync(
        string adminUserId,
        RunDeliveryJobRequestDto request,
        CancellationToken cancellationToken = default);
}
