using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Auth;

public sealed class RequestContactVerificationDto
{
    public string Email { get; set; } = string.Empty;
    public ContactVerificationChannel Channel { get; set; } = ContactVerificationChannel.Email;
}
