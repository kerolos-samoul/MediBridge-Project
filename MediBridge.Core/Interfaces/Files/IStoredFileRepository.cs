using MediBridge.Core.Enums;
using MediBridge.Core.Entities.Files;

namespace MediBridge.Core.Interfaces.Files;

public interface IStoredFileRepository
{
    Task AddStoredFileAsync(string storedFileId, StoredFileOwnerType ownerType, string ownerId, StoredFilePurpose purpose, CancellationToken cancellationToken = default);
    Task AddPendingUploadAsync(StoredFile storedFile, CancellationToken cancellationToken = default);
    Task<StoredFile?> FindByIdAsync(string storedFileId, CancellationToken cancellationToken = default);
    Task<string?> FindActiveStoredFileIdAsync(string storedFileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActiveStoredFileIdsByOwnerAsync(StoredFileOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFile>> ListByOwnerAsync(StoredFileOwnerType ownerType, string ownerId, StoredFilePurpose? purpose = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFile>> ListByCampaignAsync(string campaignId, StoredFilePurpose? purpose = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFile>> ListActiveReviewableCampaignFilesAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFile>> ListActiveOptionalCampaignFilesAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveApprovedCampaignMediaAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListStoredFileReviewIdsAsync(StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFile>> ListByReviewStatusAsync(StoredFileReviewStatus reviewStatus, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    Task<int> CountByReviewStatusAsync(StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default);
    Task MarkUploadStoredAsync(string storedFileId, string storageProvider, string storageKey, StoredFileStorageResourceType resourceType, StoredFileStorageDeliveryType deliveryType, long sizeBytes, string contentType, CancellationToken cancellationToken = default);
    Task MarkUploadFailedAsync(string storedFileId, CancellationToken cancellationToken = default);
    Task<bool> UpdateReviewSummaryAsync(string storedFileId, StoredFileReviewStatus reviewStatus, string adminUserId, string? reason, DateTime reviewedAtUtc, string expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task MarkDeletedAsync(string storedFileId, DateTime deletedAtUtc, CancellationToken cancellationToken = default);
    Task LinkReplacementAsync(string originalFileId, string replacementFileId, CancellationToken cancellationToken = default);
    Task<bool> IsOwnerActiveForNormalAccessAsync(StoredFileOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsApprovedAssetAsync(string storedFileId, CancellationToken cancellationToken = default);
}
