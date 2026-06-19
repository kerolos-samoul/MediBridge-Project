using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Policies;

public interface IAuditEventRepository
{
    Task AddAuditEventAsync(string auditEventId, string eventType, AuditOutcome outcome, DateTime createdAtUtc, CancellationToken cancellationToken = default);
    Task AddAuditEventAsync(string auditEventId, string eventType, AuditOutcome outcome, DateTime createdAtUtc, string? metadata, string? correctsAuditEventId = null, AuditTargetType? targetType = null, string? targetId = null, CancellationToken cancellationToken = default);
    Task AddPhase5AuditEventAsync(
        string auditEventId,
        string eventType,
        string? actorUserId,
        string? actorRole,
        AuditTargetType? targetType,
        string? targetId,
        AuditOutcome outcome,
        string? reason,
        string? correlationId,
        string? metadata,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default);
    Task<string?> FindAuditEventIdAsync(string auditEventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAuditEventIdsAsync(AuditTargetType? targetType = null, string? targetId = null, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAuditEventTargetIdsIncludingHistoricalTargetsAsync(AuditTargetType targetType, CancellationToken cancellationToken = default);
}
