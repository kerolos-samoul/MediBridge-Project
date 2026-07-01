using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record TargetPreviewDto(
    [property: JsonPropertyName("eligibleDoctorCount")]
    int EligibleDoctorCount,
    [property: JsonPropertyName("estimatedTotalCost")]
    decimal EstimatedTotalCost,
    [property: JsonPropertyName("currency")]
    string Currency);
