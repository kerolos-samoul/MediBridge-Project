using MediBridge.Core.Interfaces;

namespace MediBridge.APIs.Security;

public sealed class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
