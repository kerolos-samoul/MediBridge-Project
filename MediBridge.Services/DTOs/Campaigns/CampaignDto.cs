using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CampaignDto(
    [property: JsonPropertyName("campaignId")]
    string CampaignId,
    [property: JsonPropertyName("companyId")]
    string CompanyId,
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("clinicalResearchInfo")]
    string? ClinicalResearchInfo,
    [property: JsonPropertyName("status")]
    string Status);
