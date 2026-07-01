using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CampaignAssetDto(
    [property: JsonPropertyName("assetId")]
    string AssetId,
    [property: JsonPropertyName("campaignId")]
    string CampaignId,
    [property: JsonPropertyName("reviewStatus")]
    string ReviewStatus,
    [property: JsonPropertyName("reviewReason")]
    string? ReviewReason);
