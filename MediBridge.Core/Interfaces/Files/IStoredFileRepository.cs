using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Files;

public interface IStoredFileRepository
{
    Task AddStoredFileAsync(string storedFileId, StoredFileOwnerType ownerType, string ownerId, StoredFilePurpose purpose, CancellationToken cancellationToken = default);
    Task AddStoredFileAsync(StoredFile storedFile, CancellationToken cancellationToken = default);
    Task<StoredFile?> FindStoredFileAsync(string storedFileId, CancellationToken cancellationToken = default);
    Task<StoredFile?> FindStoredFileForUpdateAsync(string storedFileId, CancellationToken cancellationToken = default);
    Task<string?> FindActiveStoredFileIdAsync(string storedFileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActiveStoredFileIdsByOwnerAsync(StoredFileOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListStoredFileReviewIdsAsync(StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default);
    Task<bool> HasStoredFileAsync(
        StoredFileOwnerType ownerType,
        string ownerId,
        StoredFilePurpose purpose,
        StoredFileReviewStatus reviewStatus,
        CancellationToken cancellationToken = default);
}
