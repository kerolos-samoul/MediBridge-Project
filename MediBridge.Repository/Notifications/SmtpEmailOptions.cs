using Microsoft.Extensions.Hosting;

namespace MediBridge.Repository.Notifications;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "MediBridge";
    public bool UseStartTls { get; set; } = true;
    public bool AllowOverrideRecipientEmail { get; set; }
    public string? OverrideRecipientEmail { get; set; }

    public void Validate(IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException("SMTP host must be configured.");
        }

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("SMTP port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(FromEmail))
        {
            throw new InvalidOperationException("SMTP sender email must be configured.");
        }

        if (AllowOverrideRecipientEmail && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("SMTP recipient override is allowed only in Development.");
        }

        if (AllowOverrideRecipientEmail && string.IsNullOrWhiteSpace(OverrideRecipientEmail))
        {
            throw new InvalidOperationException("SMTP recipient override email must be configured when override is enabled.");
        }
    }
}
