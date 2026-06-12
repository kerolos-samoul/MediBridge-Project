namespace MediBridge.Services.Config;

public sealed class ContactVerificationOptions
{
    public const string SectionName = "ContactVerification";

    public int OtpLength { get; set; } = 6;
    public int ExpirationMinutes { get; set; } = 10;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public string? OverrideRecipientEmail { get; set; } = "medibridge7@gmail.com";
    public bool AllowOverrideRecipientEmail { get; set; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (OtpLength is < 4 or > 10)
        {
            errors.Add("OTP length must be between 4 and 10 digits.");
        }

        if (ExpirationMinutes <= 0)
        {
            errors.Add("OTP expiration minutes must be positive.");
        }

        if (ResendCooldownSeconds < 0)
        {
            errors.Add("OTP resend cooldown seconds cannot be negative.");
        }

        if (MaxAttempts <= 0)
        {
            errors.Add("OTP max attempts must be positive.");
        }

        if (AllowOverrideRecipientEmail && string.IsNullOrWhiteSpace(OverrideRecipientEmail))
        {
            errors.Add("Override recipient email is required when override is allowed.");
        }

        return errors;
    }
}
