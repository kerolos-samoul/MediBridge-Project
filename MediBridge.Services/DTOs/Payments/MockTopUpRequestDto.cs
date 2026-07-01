using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Payments;

public sealed record MockTopUpRequestDto(
    [property: JsonPropertyName("amount")]
    decimal Amount,
    [property: JsonPropertyName("currency")]
    string Currency);
