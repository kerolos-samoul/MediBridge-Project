using MailKit.Net.Smtp;
using MailKit.Security;
using MediBridge.Core.Interfaces.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MediBridge.Repository.Notifications;

public sealed class MailKitEmailDelivery : IEmailDelivery
{
    private readonly SmtpEmailOptions options;
    private readonly IHostEnvironment environment;

    public MailKitEmailDelivery(IOptions<SmtpEmailOptions> options, IHostEnvironment environment)
    {
        this.options = options.Value;
        this.environment = environment;
        this.options.Validate(environment);
    }

    public Task SendContactVerificationAsync(
        string destination,
        string oneTimeCode,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(
            ResolveRecipient(destination),
            "Your MediBridge verification code",
            $"Your MediBridge verification code expires at {expiresAtUtc:O}.",
            $"<p>Your MediBridge verification code is <strong>{oneTimeCode}</strong>.</p><p>It expires at {expiresAtUtc:O}.</p>");
        return SendAsync(message, cancellationToken);
    }

    public Task SendPasswordResetAsync(
        string destination,
        string resetToken,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(
            ResolveRecipient(destination),
            "Reset your MediBridge password",
            $"Use your MediBridge password reset link before {expiresAtUtc:O}.",
            $"<p>Use this password reset token before {expiresAtUtc:O}.</p><p>{resetToken}</p>");
        return SendAsync(message, cancellationToken);
    }

    private MimeMessage CreateMessage(string destination, string subject, string textBody, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
        message.To.Add(MailboxAddress.Parse(destination));
        message.Subject = subject;
        message.Body = new BodyBuilder
        {
            TextBody = textBody,
            HtmlBody = htmlBody
        }.ToMessageBody();
        return message;
    }

    private async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();
        try
        {
            var socketOptions = options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(options.Host, options.Port, socketOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }

    private string ResolveRecipient(string destination)
    {
        if (options.AllowOverrideRecipientEmail && environment.IsDevelopment())
        {
            return options.OverrideRecipientEmail!;
        }

        return destination;
    }
}
