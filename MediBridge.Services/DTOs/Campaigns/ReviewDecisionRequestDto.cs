using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record ReviewDecisionRequestDto(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("notes")] string? Notes);
