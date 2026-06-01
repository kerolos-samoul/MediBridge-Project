using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Auth;

public sealed class VerifyContactRequestDto
{
    public ContactVerificationChannel Channel { get; set; }
    public string VerificationToken { get; set; } = string.Empty;
}
