using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Interfaces;

public interface IEmailSender
{
    Task SendAsync(EmailMessageDto message, CancellationToken cancellationToken = default);
}
