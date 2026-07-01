using MediBridge.Services.DTOs.Pricing;

namespace MediBridge.Services.Interfaces;

public interface IAdminPricingService
{
    Task<DoctorPriceDto> SetDoctorPriceAsync(
        string adminUserId,
        string doctorId,
        SetDoctorPriceRequestDto request,
        CancellationToken cancellationToken = default);
}
