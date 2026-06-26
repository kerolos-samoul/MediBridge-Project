using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Auth;

public sealed class RegistrationResultDto
{
    public string UserId { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public AccountStatus AccountStatus { get; set; }
}
