namespace MediBridge.Services.DTOs.Auth;

public sealed class RefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}
