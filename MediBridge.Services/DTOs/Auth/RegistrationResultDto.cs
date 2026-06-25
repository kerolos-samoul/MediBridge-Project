using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Auth;

public sealed class RegistrationResultDto
{
    public string UserId { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public AccountStatus AccountStatus { get; set; }
    public bool VerificationRequired { get; set; }
    public ContactVerificationChannel? VerificationChannel { get; set; }
    public string? MaskedVerificationDestination { get; set; }
    public DateTime? VerificationExpiresAtUtc { get; set; }
    public string VerificationDeliveryStatus { get; set; } = "NotRequired";
}
