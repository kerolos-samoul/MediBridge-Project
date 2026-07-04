using MediBridge.Services.DTOs.Messaging;

namespace MediBridge.Services.Interfaces;

public interface IDailyDeliveryInjectorService
{
    Task<DailyInjectorResultDto> RunAsync(CancellationToken cancellationToken = default);
}
