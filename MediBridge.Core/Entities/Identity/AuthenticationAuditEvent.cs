using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Identity;

public sealed class AuthenticationAuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AuthAuditEventType EventType { get; set; }
    public string? ActorUserId { get; set; }
    public string? TargetUserId { get; set; }
    public UserRole? Role { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
