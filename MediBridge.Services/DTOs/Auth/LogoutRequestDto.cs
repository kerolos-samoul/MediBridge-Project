namespace MediBridge.Services.DTOs.Auth;

public sealed class LogoutRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}
