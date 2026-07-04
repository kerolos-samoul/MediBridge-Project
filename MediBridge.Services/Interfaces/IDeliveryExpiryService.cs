using MediBridge.Services.DTOs.Messaging;

namespace MediBridge.Services.Interfaces;

public interface IDeliveryExpiryService
{
    Task<DeliveryJobResultDto> RunAsync(CancellationToken cancellationToken = default);
}
