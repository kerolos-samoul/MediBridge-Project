using System.ComponentModel.DataAnnotations;
using MediBridge.Services.Config;

namespace MediBridge.APIs.Config;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = AuthTokenOptions.DefaultAccessTokenMinutes;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = AuthTokenOptions.DefaultRefreshTokenDays;
}
