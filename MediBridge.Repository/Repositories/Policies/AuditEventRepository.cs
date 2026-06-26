using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Policies;

public sealed class AuditEventRepository : IAuditEventRepository
{
    private readonly MediBridgeDbContext context;

    public AuditEventRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAuditEventAsync(string auditEventId, string eventType, AuditOutcome outcome, DateTime createdAtUtc, CancellationToken cancellationToken = default)
    {
        await AddAuditEventAsync(auditEventId, eventType, outcome, createdAtUtc, metadata: null, correctsAuditEventId: null, targetType: null, targetId: null, cancellationToken);
    }

    public async Task AddAuditEventAsync(string auditEventId, string eventType, AuditOutcome outcome, DateTime createdAtUtc, string? metadata, string? correctsAuditEventId = null, AuditTargetType? targetType = null, string? targetId = null, CancellationToken cancellationToken = default)
    {
        await context.AuditEvents.AddAsync(new AuditEvent
        {
            Id = auditEventId,
            EventType = eventType,
            Outcome = outcome,
            CreatedAtUtc = createdAtUtc,
            Metadata = AuditMetadataRules.EnsureSafe(metadata, nameof(metadata)),
            CorrectsAuditEventId = correctsAuditEventId,
            TargetType = targetType,
            TargetId = targetId
        }, cancellationToken);
    }

    public async Task AddPhase5AuditEventAsync(
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
        CancellationToken cancellationToken = default)
    {
        await context.AuditEvents.AddAsync(new AuditEvent
        {
            Id = auditEventId,
            EventType = eventType,
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = outcome,
            Reason = reason,
            CorrelationId = correlationId,
            Metadata = AuditMetadataRules.EnsureSafe(metadata, nameof(metadata)),
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);
    }

    public Task<string?> FindAuditEventIdAsync(string auditEventId, CancellationToken cancellationToken = default)
    {
        return context.AuditEvents
            .Where(auditEvent => auditEvent.Id == auditEventId)
            .Select(auditEvent => auditEvent.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAuditEventIdsAsync(AuditTargetType? targetType = null, string? targetId = null, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default)
    {
        var query = context.AuditEvents.AsQueryable();
        if (targetType is not null)
        {
            query = query.Where(auditEvent => auditEvent.TargetType == targetType);
        }

        if (targetId is not null)
        {
            query = query.Where(auditEvent => auditEvent.TargetId == targetId);
        }

        if (createdFromUtc is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc >= createdFromUtc);
        }

        if (createdToUtc is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc <= createdToUtc);
        }

        return await query
            .OrderBy(auditEvent => auditEvent.CreatedAtUtc)
            .Select(auditEvent => auditEvent.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAuditEventTargetIdsIncludingHistoricalTargetsAsync(AuditTargetType targetType, CancellationToken cancellationToken = default)
    {
        return await context.AuditEvents
            .Where(auditEvent => auditEvent.TargetType == targetType && auditEvent.TargetId != null)
            .OrderBy(auditEvent => auditEvent.CreatedAtUtc)
            .Select(auditEvent => auditEvent.TargetId!)
            .ToListAsync(cancellationToken);
    }
}
