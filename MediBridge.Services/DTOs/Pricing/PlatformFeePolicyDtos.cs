namespace MediBridge.Services.DTOs.Pricing;

public sealed record SetPlatformFeePolicyRequestDto(decimal? FeePercent, string? Reason);

public sealed record PlatformFeePolicyDto(
    string PolicyId,
    decimal FeePercent,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc);
