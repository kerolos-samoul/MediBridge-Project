namespace MediBridge.Core.Entities.Campaigns;

public sealed class CampaignTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CampaignId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string SpecializationSnapshot { get; set; } = string.Empty;
    public int ExperienceYearsSnapshot { get; set; }
    public string LocationSnapshot { get; set; } = string.Empty;
    public decimal ActivityScoreSnapshot { get; set; }
    public decimal PricePerMessageSnapshot { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
