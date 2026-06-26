using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Files;

public sealed class StoredFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public StoredFileOwnerType OwnerType { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public StoredFilePurpose Purpose { get; set; }
    public string? RelatedCampaignId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = "Cloudinary";
    public StoredFileStorageResourceType StorageResourceType { get; set; } = StoredFileStorageResourceType.Raw;
    public StoredFileStorageDeliveryType StorageDeliveryType { get; set; } = StoredFileStorageDeliveryType.Private;
    public StoredFileVisibility Visibility { get; set; } = StoredFileVisibility.Private;
    public StoredFileReviewStatus ReviewStatus { get; set; } = StoredFileReviewStatus.Pending;
    public StoredFileUploadStatus UploadStatus { get; set; } = StoredFileUploadStatus.PendingUpload;
    public StoredFileSafetyScanStatus SafetyScanStatus { get; set; } = StoredFileSafetyScanStatus.Deferred;
    public DateTime? SafetyScanCheckedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByAdminId { get; set; }
    public string? ReviewReason { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? ReplacedByFileId { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
