using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Messaging;

public sealed class DoctorMessageQueue : IConcurrencyTrackedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CampaignSubmittedAtUtc { get; init; }
    public QueueItemStatus Status { get; set; } = QueueItemStatus.Queued;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();

    public void MarkActivated(DateTime activatedAtUtc)
    {
        EnsureUtc(activatedAtUtc, nameof(activatedAtUtc));
        EnsureQueued();
        Status = QueueItemStatus.Activated;
        UpdatedAtUtc = activatedAtUtc;
    }

    public void MarkCancelled(DateTime cancelledAtUtc)
    {
        EnsureUtc(cancelledAtUtc, nameof(cancelledAtUtc));
        EnsureQueued();
        Status = QueueItemStatus.Cancelled;
        UpdatedAtUtc = cancelledAtUtc;
    }

    private void EnsureQueued()
    {
        if (Status != QueueItemStatus.Queued)
        {
            throw new InvalidOperationException($"Queue item in state '{Status}' cannot transition again.");
        }
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The transition timestamp must be UTC.", parameterName);
        }
    }
}
