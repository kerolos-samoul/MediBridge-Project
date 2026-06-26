using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Interfaces;

namespace MediBridge.IntegrationTests.TestHost;

public sealed class TestEmailSink
{
    private readonly List<EmailMessageDto> messages = [];
    private readonly object gate = new();

    public IReadOnlyList<EmailMessageDto> Messages
    {
        get
        {
            lock (gate)
            {
                return messages.ToArray();
            }
        }
    }

    public void Add(EmailMessageDto message)
    {
        lock (gate)
        {
            messages.Add(message);
        }
    }

    public bool ThrowOnSend { get; set; }
}

public sealed class TestEmailSender : IEmailSender
{
    private readonly TestEmailSink sink;

    public TestEmailSender(TestEmailSink sink)
    {
        this.sink = sink;
    }

    public Task SendAsync(EmailMessageDto message, CancellationToken cancellationToken = default)
    {
        if (sink.ThrowOnSend)
        {
            throw new InvalidOperationException("Test email failure.");
        }

        sink.Add(message);
        return Task.CompletedTask;
    }
}
