using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed class CreateCampaignDraftRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ClinicalResearchInfo { get; set; }
}

public sealed class CampaignDraftDto
{
    public string CampaignId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ClinicalResearchInfo { get; set; }
    public CampaignStatus Status { get; set; }
}
