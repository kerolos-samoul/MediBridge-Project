namespace MediBridge.Services.DTOs.Pricing;

public sealed record SetDoctorDeliverySettingsRequestDto(
    int? DailyMessageLimit,
    int? MinimumWeeklyRequirement,
    string? Reason);

public sealed record DoctorDeliverySettingsDto(
    string DoctorId,
    int DailyMessageLimit,
    int MinimumWeeklyRequirement,
    bool IsDeliveryEligible,
    string EligibilityState);
