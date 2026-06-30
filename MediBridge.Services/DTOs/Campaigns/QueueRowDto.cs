using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record QueueRowDto(
    [property: JsonPropertyName("queueId")] string QueueId,
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("doctorId")] string DoctorId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("campaignSubmittedAtUtc")] DateTime CampaignSubmittedAtUtc,
    [property: JsonPropertyName("queuedAtUtc")] DateTime QueuedAtUtc);
