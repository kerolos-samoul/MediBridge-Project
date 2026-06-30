namespace MediBridge.Core.Entities.Campaigns;

public sealed class CampaignSubmissionAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CampaignId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
    public int TargetCount { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
