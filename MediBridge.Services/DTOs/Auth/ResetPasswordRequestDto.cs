namespace MediBridge.Services.DTOs.Auth;

public sealed class ResetPasswordRequestDto
{
    public string ResetToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
