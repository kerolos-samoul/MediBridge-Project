using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Auth;

public sealed class AuthResultDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
