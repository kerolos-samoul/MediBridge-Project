using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Files;

public sealed class StoredFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public StoredFileOwnerType OwnerType { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public StoredFilePurpose Purpose { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string StorageResourceType { get; set; } = "raw";
    public StorageObjectState StorageState { get; set; } = StorageObjectState.Active;
    public string? SupersededByFileId { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public StoredFileVisibility Visibility { get; set; } = StoredFileVisibility.Private;
    public StoredFileReviewStatus ReviewStatus { get; set; } = StoredFileReviewStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByAdminId { get; set; }
    public string? ReviewReason { get; set; }
}
