using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Campaigns;

public sealed class Campaign : ISoftDeleteRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? MediaFileId { get; set; }
    public string? VoiceNoteFileId { get; set; }
    public string? ClinicalResearchInfo { get; set; }
    public string Description { get; set; } = string.Empty;
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
