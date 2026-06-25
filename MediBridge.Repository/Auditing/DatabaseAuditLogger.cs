using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Repository.Data;
using PolicyAuditEvent = MediBridge.Core.Entities.Policies.AuditEvent;

namespace MediBridge.Repository.Auditing;

public sealed class DatabaseAuditLogger : IAuditLogger
{
    private readonly MediBridgeDbContext context;

    public DatabaseAuditLogger(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task LogAsync(Core.Interfaces.AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await context.AuditEvents.AddAsync(new PolicyAuditEvent
        {
            EventType = auditEvent.Action,
            ActorUserId = auditEvent.ActorUserId,
            ActorRole = auditEvent.ActorRole,
            TargetType = MapTargetType(auditEvent.SubjectType),
            TargetId = auditEvent.SubjectId,
            Outcome = auditEvent.Category == AuditEventCategory.System ? AuditOutcome.Failed : AuditOutcome.Info,
            CorrelationId = auditEvent.CorrelationId,
            CreatedAtUtc = auditEvent.CreatedAtUtc
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static AuditTargetType? MapTargetType(string? subjectType) =>
        string.Equals(subjectType, "StoredFileObject", StringComparison.OrdinalIgnoreCase)
            ? AuditTargetType.StoredFile
            : null;
}
