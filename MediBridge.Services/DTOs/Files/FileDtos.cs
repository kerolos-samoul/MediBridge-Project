using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Files;

public sealed record FileDto(
    string Id,
    StoredFilePurpose Purpose,
    StoredFileOwnerType OwnerType,
    string OwnerId,
    string? RelatedCampaignId,
    string? ReplacedFileId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    StoredFileReviewStatus ReviewStatus,
    StoredFileUploadStatus UploadStatus,
    StoredFileSafetyScanStatus SafetyScanStatus,
    DateTime CreatedAtUtc);

public sealed record FileAccessGrantDto(string Url, DateTime ExpiresAtUtc);

public sealed record FileReviewRequestDto(FileReviewDecision Decision, string? Reason, string? Notes = null, string? CorrectsReviewId = null);

public sealed record FileReviewDto(string Id, string StoredFileId, FileReviewDecision Decision, string? Reason, string ReviewedByAdminId, DateTime CreatedAtUtc, string? CorrectsReviewId);

public sealed record PendingFileReviewPageDto(IReadOnlyList<FileDto> Items, int PageNumber, int PageSize, int TotalCount);

public sealed record DeleteFileResultDto(string Id, StoredFileUploadStatus UploadStatus, DateTime? DeletedAtUtc);
