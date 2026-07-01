using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed record ReviewFileDto(
    [property: JsonPropertyName("fileId")]
    string FileId,
    [property: JsonPropertyName("purpose")]
    string Purpose,
    [property: JsonPropertyName("originalFileName")]
    string OriginalFileName,
    [property: JsonPropertyName("contentType")]
    string ContentType,
    [property: JsonPropertyName("sizeBytes")]
    long SizeBytes,
    [property: JsonPropertyName("reviewStatus")]
    string ReviewStatus,
    [property: JsonPropertyName("accessUrl")]
    string AccessUrl,
    [property: JsonPropertyName("accessExpiresAtUtc")]
    DateTime AccessExpiresAtUtc);
