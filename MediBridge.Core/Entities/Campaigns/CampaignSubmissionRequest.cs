using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Campaigns;

public sealed class CampaignSubmissionRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? CampaignId { get; set; }
    public string? RequestHash { get; set; }
    public CampaignSubmissionRequestStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
