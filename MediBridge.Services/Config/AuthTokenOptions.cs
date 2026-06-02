namespace MediBridge.Services.Config;

public sealed class AuthTokenOptions
{
    public const string SectionName = "Jwt";
    public const int DefaultAccessTokenMinutes = 60;
    public const int DefaultRefreshTokenDays = 7;

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = DefaultAccessTokenMinutes;
    public int RefreshTokenDays { get; set; } = DefaultRefreshTokenDays;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("JWT issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("JWT audience must be configured.");
        }

        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            throw new InvalidOperationException("JWT signing key must be configured.");
        }

        if (AccessTokenMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("JWT access token lifetime must be between 1 and 1440 minutes.");
        }

        if (RefreshTokenDays is < 1 or > 365)
        {
            throw new InvalidOperationException("JWT refresh token lifetime must be between 1 and 365 days.");
        }
    }
}
