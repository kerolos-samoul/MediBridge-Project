using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record UpdateCampaignRequestDto(
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("clinicalResearchInfo")]
    string? ClinicalResearchInfo);
