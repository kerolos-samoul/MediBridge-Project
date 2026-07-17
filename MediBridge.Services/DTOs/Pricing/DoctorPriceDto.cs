using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Pricing;

public sealed record DoctorPriceDto(
    [property: JsonPropertyName("doctorId")] string DoctorId,
    [property: JsonPropertyName("pricePerMessage")] decimal? PricePerMessage,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("pricingIsActive")] bool PricingIsActive);

public sealed record DeactivateDoctorPricingRequestDto(
    [property: JsonPropertyName("reason")] string? Reason);
