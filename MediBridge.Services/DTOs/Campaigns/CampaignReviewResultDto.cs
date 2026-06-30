using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CampaignReviewResultDto(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("queuedCount")] int QueuedCount,
    [property: JsonPropertyName("decisionTimeUtc")] DateTime DecisionTimeUtc,
    [property: JsonPropertyName("canResubmit")] bool CanResubmit);
