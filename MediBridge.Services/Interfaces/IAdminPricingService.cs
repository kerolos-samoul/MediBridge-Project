using MediBridge.Services.DTOs.Pricing;

namespace MediBridge.Services.Interfaces;

public interface IAdminPricingService
{
    Task<DoctorPriceDto> SetDoctorPriceAsync(
        string adminUserId,
        string doctorId,
        SetDoctorPriceRequestDto request,
        CancellationToken cancellationToken = default);

    Task<DoctorPriceDto> DeactivateDoctorPricingAsync(
        string adminUserId,
        string doctorId,
        DeactivateDoctorPricingRequestDto request,
        CancellationToken cancellationToken = default);

    Task<DoctorDeliverySettingsDto> GetDoctorDeliverySettingsAsync(
        string adminUserId,
        string doctorId,
        CancellationToken cancellationToken = default);

    Task<DoctorDeliverySettingsDto> SetDoctorDeliverySettingsAsync(
        string adminUserId,
        string doctorId,
        SetDoctorDeliverySettingsRequestDto request,
        CancellationToken cancellationToken = default);
}
