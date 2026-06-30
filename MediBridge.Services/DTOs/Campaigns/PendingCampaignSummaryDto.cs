using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record PendingCampaignSummaryDto(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("companyId")] string CompanyId,
    [property: JsonPropertyName("companyName")] string CompanyName,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("descriptionSummary")] string DescriptionSummary,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("submittedAtUtc")] DateTime SubmittedAtUtc,
    [property: JsonPropertyName("targetCount")] int TargetCount,
    [property: JsonPropertyName("hasApprovedMedia")] bool HasApprovedMedia,
    [property: JsonPropertyName("reviewReadinessIssues")] IReadOnlyList<string> ReviewReadinessIssues);
