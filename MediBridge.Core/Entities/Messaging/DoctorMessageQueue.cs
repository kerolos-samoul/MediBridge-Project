using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Messaging;

public sealed class DoctorMessageQueue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CampaignSubmittedAtUtc { get; set; }
    public QueueItemStatus Status { get; set; } = QueueItemStatus.Queued;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
