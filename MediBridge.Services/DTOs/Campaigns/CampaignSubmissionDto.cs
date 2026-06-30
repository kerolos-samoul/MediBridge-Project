using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CampaignSubmissionDto(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("targetCount")] int TargetCount,
    [property: JsonPropertyName("estimatedCost")] decimal EstimatedCost,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("submittedAtUtc")] DateTime SubmittedAtUtc);
