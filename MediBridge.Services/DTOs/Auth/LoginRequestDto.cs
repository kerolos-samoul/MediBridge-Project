namespace MediBridge.Services.DTOs.Auth;

public sealed class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
