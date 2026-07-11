using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Messaging;

public sealed class DeliveryInteraction : IConcurrencyTrackedRecord
{
    public const int MaxFeedbackLength = 2000;
    public const int MaxHashLength = 128;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeliveryId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public DeliveryInteractionOutcome Outcome { get; set; }
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string? FeedbackText { get; set; }
    public bool FeedbackQualifiesForScore { get; set; }
    public string ChargeTransactionId { get; set; } = string.Empty;
    public string EarnTransactionId { get; set; } = string.Empty;
    public string? AuditEventId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();
}
