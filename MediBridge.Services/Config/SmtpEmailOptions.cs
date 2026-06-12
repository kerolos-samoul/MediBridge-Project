namespace MediBridge.Services.Config;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "MediBridge";

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add("SMTP host is required.");
        }

        if (Port <= 0)
        {
            errors.Add("SMTP port must be positive.");
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            errors.Add("SMTP username is required.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            errors.Add("SMTP password is required.");
        }

        if (string.IsNullOrWhiteSpace(FromEmail))
        {
            errors.Add("SMTP from email is required.");
        }

        return errors;
    }
}
