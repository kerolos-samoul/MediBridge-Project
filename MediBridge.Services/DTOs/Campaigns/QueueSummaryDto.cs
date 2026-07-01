using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record QueueSummaryDto(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("queuedCount")] int QueuedCount,
    [property: JsonPropertyName("activatedCount")] int ActivatedCount,
    [property: JsonPropertyName("cancelledCount")] int CancelledCount,
    [property: JsonPropertyName("expiredCount")] int ExpiredCount,
    [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc);
