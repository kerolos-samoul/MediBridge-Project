namespace MediBridge.Services.DTOs.Pricing;

public sealed record SetDoctorPriceRequestDto(decimal? PricePerMessage, string? Reason);
