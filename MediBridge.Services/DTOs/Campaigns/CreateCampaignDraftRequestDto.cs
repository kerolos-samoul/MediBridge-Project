namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CreateCampaignDraftRequestDto(
    string Title,
    string Description,
    string? ClinicalResearchInfo);
