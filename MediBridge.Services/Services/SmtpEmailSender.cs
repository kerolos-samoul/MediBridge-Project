using System.Net;
using System.Net.Mail;
using MediBridge.Services.Config;
using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace MediBridge.Services.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailOptions options;
    private readonly ILogger<SmtpEmailSender> logger;

    public SmtpEmailSender(SmtpEmailOptions options, ILogger<SmtpEmailSender> logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public async Task SendAsync(EmailMessageDto message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "SMTP email send starting. Host: {SmtpHost}, Port: {SmtpPort}, EnableSsl: {EnableSsl}, FromEmail: {FromEmail}, RecipientEmail: {RecipientEmail}, Subject: {Subject}",
            options.Host,
            options.Port,
            options.EnableSsl,
            options.FromEmail,
            message.RecipientEmail,
            message.Subject);

        using var smtpClient = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            Credentials = new NetworkCredential(options.Username, options.Password)
        };

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(options.FromEmail, options.FromDisplayName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };

        mailMessage.To.Add(message.RecipientEmail);
        await smtpClient.SendMailAsync(mailMessage, cancellationToken);

        logger.LogInformation(
            "SMTP email send succeeded. RecipientEmail: {RecipientEmail}, Subject: {Subject}",
            message.RecipientEmail,
            message.Subject);
    }
}
