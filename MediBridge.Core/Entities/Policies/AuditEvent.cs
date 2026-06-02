using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Policies;

public sealed class AuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string? ActorRole { get; set; }
    public AuditTargetType? TargetType { get; set; }
    public string? TargetId { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CorrectsAuditEventId { get; set; }
}
