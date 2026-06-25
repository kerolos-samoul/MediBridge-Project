namespace MediBridge.Services.Config;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public int ExpirationMinutes { get; set; } = 60;
    public string ResetLinkBaseUri { get; set; } = string.Empty;

    public void Validate()
    {
        if (ExpirationMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Password reset expiration must be between 1 and 1440 minutes.");
        }

        if (!Uri.TryCreate(ResetLinkBaseUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Password reset link base URI must be an absolute HTTPS URI.");
        }
    }
}
