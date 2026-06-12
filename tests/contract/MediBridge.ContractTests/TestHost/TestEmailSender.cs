using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Interfaces;

namespace MediBridge.ContractTests.TestHost;

public sealed class TestEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessageDto message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
