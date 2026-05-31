namespace MediBridge.Core.Interfaces;

public interface IAuditLogger
{
    Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed record AuditEvent(
    AuditEventCategory Category,
    string Action,
    string? ActorUserId,
    string? ActorRole,
    string? SubjectType,
    string? SubjectId,
    string? CorrelationId,
    DateTime CreatedAtUtc);

public enum AuditEventCategory
{
    AuthenticationSensitive,
    AdminAction,
    Financial,
    DocumentReview,
    System
}
