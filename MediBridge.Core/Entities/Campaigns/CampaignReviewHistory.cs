using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Campaigns;

public sealed class CampaignReviewHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CampaignId { get; set; } = string.Empty;
    public string AdminUserId { get; set; } = string.Empty;
    public CampaignReviewDecision Decision { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CorrectsHistoryId { get; set; }
}
