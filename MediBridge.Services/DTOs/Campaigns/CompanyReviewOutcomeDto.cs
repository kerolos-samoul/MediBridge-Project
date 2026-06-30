using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CompanyReviewOutcomeDto(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("publicReason")] string? PublicReason,
    [property: JsonPropertyName("decisionTimeUtc")] DateTime? DecisionTimeUtc,
    [property: JsonPropertyName("canEdit")] bool CanEdit,
    [property: JsonPropertyName("canResubmit")] bool CanResubmit,
    [property: JsonPropertyName("queuedCount")] int QueuedCount);
