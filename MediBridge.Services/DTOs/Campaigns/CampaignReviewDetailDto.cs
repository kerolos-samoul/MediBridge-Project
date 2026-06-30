using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CampaignReviewDetailDto(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("companyId")] string CompanyId,
    [property: JsonPropertyName("companyName")] string CompanyName,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("submittedAtUtc")] DateTime SubmittedAtUtc,
    [property: JsonPropertyName("targetCount")] int TargetCount,
    [property: JsonPropertyName("reviewableMediaAssets")] IReadOnlyList<ReviewFileDto> ReviewableMediaAssets,
    [property: JsonPropertyName("optionalFiles")] IReadOnlyList<ReviewFileDto> OptionalFiles,
    [property: JsonPropertyName("reviewReadinessIssues")] IReadOnlyList<string> ReviewReadinessIssues);
