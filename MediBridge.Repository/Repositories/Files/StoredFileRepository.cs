using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Files;

public sealed class StoredFileRepository : IStoredFileRepository
{
    private readonly MediBridgeDbContext context;

    public StoredFileRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddStoredFileAsync(string storedFileId, StoredFileOwnerType ownerType, string ownerId, StoredFilePurpose purpose, CancellationToken cancellationToken = default)
    {
        await context.StoredFiles.AddAsync(new StoredFile
        {
            Id = storedFileId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            Purpose = purpose,
            OriginalFileName = "phase3-sample.dat",
            ContentType = "application/octet-stream",
            SizeBytes = 128,
            StorageKey = $"phase3/{storedFileId}",
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Pending
        }, cancellationToken);
    }

    public async Task AddStoredFileAsync(StoredFile storedFile, CancellationToken cancellationToken = default)
    {
        await context.StoredFiles.AddAsync(storedFile, cancellationToken);
    }

    public Task<StoredFile?> FindStoredFileAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return context.StoredFiles.FirstOrDefaultAsync(file => file.Id == storedFileId, cancellationToken);
    }

    public Task<StoredFile?> FindStoredFileForUpdateAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return context.StoredFiles
            .FromSqlInterpolated($"""
                SELECT *
                FROM [StoredFiles] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {storedFileId}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<string?> FindActiveStoredFileIdAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return context.StoredFiles
            .Where(file => file.Id == storedFileId
                && file.StorageState == StorageObjectState.Active
                && file.DeletedAtUtc == null)
            .Select(file => file.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActiveStoredFileIdsByOwnerAsync(StoredFileOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return await context.StoredFiles
            .Where(file => file.OwnerType == ownerType && file.OwnerId == ownerId)
            .OrderBy(file => file.CreatedAtUtc)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListStoredFileReviewIdsAsync(StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default)
    {
        return await context.StoredFiles
            .Where(file => file.ReviewStatus == reviewStatus)
            .OrderBy(file => file.CreatedAtUtc)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasStoredFileAsync(
        StoredFileOwnerType ownerType,
        string ownerId,
        StoredFilePurpose purpose,
        StoredFileReviewStatus reviewStatus,
        CancellationToken cancellationToken = default)
    {
        return context.StoredFiles.AnyAsync(
            file => file.OwnerType == ownerType
                && file.OwnerId == ownerId
                && file.Purpose == purpose
                && file.ReviewStatus == reviewStatus
                && file.StorageState == StorageObjectState.Active
                && file.DeletedAtUtc == null,
            cancellationToken);
    }
}
