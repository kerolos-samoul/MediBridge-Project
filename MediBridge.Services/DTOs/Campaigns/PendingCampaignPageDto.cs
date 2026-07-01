using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record PendingCampaignPageDto(
    [property: JsonPropertyName("items")]
    IReadOnlyList<PendingCampaignSummaryDto> Items,
    [property: JsonPropertyName("pageNumber")]
    int PageNumber,
    [property: JsonPropertyName("pageSize")]
    int PageSize,
    [property: JsonPropertyName("totalCount")]
    int TotalCount);
