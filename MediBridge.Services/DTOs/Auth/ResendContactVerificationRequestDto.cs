using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Auth;

public sealed class ResendContactVerificationRequestDto
{
    public string Contact { get; set; } = string.Empty;
    public ContactVerificationChannel Channel { get; set; }
}
