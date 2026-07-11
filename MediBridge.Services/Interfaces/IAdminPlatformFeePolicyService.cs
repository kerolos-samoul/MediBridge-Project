using MediBridge.Services.DTOs.Pricing;

namespace MediBridge.Services.Interfaces;

public interface IAdminPlatformFeePolicyService
{
    Task<PlatformFeePolicyDto?> GetCurrentAsync(
        string adminUserId,
        CancellationToken cancellationToken = default);

    Task<PlatformFeePolicyDto> SetAsync(
        string adminUserId,
        SetPlatformFeePolicyRequestDto request,
        CancellationToken cancellationToken = default);
}
