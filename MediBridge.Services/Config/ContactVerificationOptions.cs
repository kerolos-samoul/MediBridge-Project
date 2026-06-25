using System.Text;

namespace MediBridge.Services.Config;

public sealed class ContactVerificationOptions
{
    public const string SectionName = "ContactVerification";

    public int OtpLength { get; set; } = 6;
    public int ExpirationMinutes { get; set; } = 10;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public int MaxResendsPerWindow { get; set; } = 5;
    public int ResendWindowMinutes { get; set; } = 60;
    public string OneTimeSecretHashingKey { get; set; } = string.Empty;

    public void Validate(string? jwtSigningKey = null)
    {
        if (OtpLength is < 6 or > 10)
        {
            throw new InvalidOperationException("Contact verification OTP length must be between 6 and 10 digits.");
        }

        if (ExpirationMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Contact verification expiration must be between 1 and 1440 minutes.");
        }

        if (ResendCooldownSeconds is < 0 or > 3600)
        {
            throw new InvalidOperationException("Contact verification resend cooldown must be between 0 and 3600 seconds.");
        }

        if (MaxAttempts is < 1 or > 20)
        {
            throw new InvalidOperationException("Contact verification max attempts must be between 1 and 20.");
        }

        if (MaxResendsPerWindow is < 0 or > 50)
        {
            throw new InvalidOperationException("Contact verification resend limit must be between 0 and 50.");
        }

        if (ResendWindowMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Contact verification resend window must be between 1 and 1440 minutes.");
        }

        if (Encoding.UTF8.GetByteCount(OneTimeSecretHashingKey) < 32)
        {
            throw new InvalidOperationException("Contact verification hashing key must be at least 32 bytes.");
        }

        if (!string.IsNullOrWhiteSpace(jwtSigningKey)
            && string.Equals(OneTimeSecretHashingKey, jwtSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Contact verification hashing key must not reuse the JWT signing key.");
        }
    }
}
