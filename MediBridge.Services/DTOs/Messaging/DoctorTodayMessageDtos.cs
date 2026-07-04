using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Messaging;

public sealed record TodayInboxDto(
    DateOnly BusinessDateEgypt,
    IReadOnlyList<TodayMessageDto> Items,
    string? NextCursor);

public sealed record TodayMessageDto(
    string DeliveryId,
    string CampaignId,
    DeliveryStatus Status,
    DateOnly DeliveryDateEgypt,
    DateTime DeliveredAtUtc,
    string Title,
    string Description,
    string? ClinicalResearchInfo,
    IReadOnlyList<DeliveryAssetDto> Assets);

public sealed record DeliveryAssetDto(
    string FileId,
    StoredFilePurpose Purpose,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    StoredFileReviewStatus ReviewStatus,
    string AccessPath);

public sealed record DeliveryAssetAccessGrantDto(string AccessUrl, DateTime ExpiresAtUtc);

internal sealed record TodayInboxCursorPayload(
    string DoctorId,
    DateOnly BusinessDateEgypt,
    DateTime DeliveredAtUtc,
    string DeliveryId);
