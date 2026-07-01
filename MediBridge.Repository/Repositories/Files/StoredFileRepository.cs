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
        return context.StoredFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(file => file.Id == storedFileId, cancellationToken);
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
                && file.DeletedAtUtc == null
                && file.SupersededByFileId == null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFile>> ListActiveReviewableCampaignFilesAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveCampaignFiles(campaignId)
            .Where(file => file.Purpose == StoredFilePurpose.CampaignMedia
                && (file.ReviewStatus == StoredFileReviewStatus.Pending
                    || file.ReviewStatus == StoredFileReviewStatus.Approved))
            .OrderBy(file => file.CreatedAtUtc)
            .ThenBy(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasActiveApprovedCampaignMediaAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return ActiveCampaignFiles(campaignId)
            .AnyAsync(file => file.Purpose == StoredFilePurpose.CampaignMedia
                && file.ReviewStatus == StoredFileReviewStatus.Approved,
                cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFile>> ListActiveOptionalCampaignFilesAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveCampaignFiles(campaignId)
            .Where(file => file.Purpose == StoredFilePurpose.VoiceNote
                || file.Purpose == StoredFilePurpose.ClinicalResearchAttachment)
            .OrderBy(file => file.CreatedAtUtc)
            .ThenBy(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<StoredFile> ActiveCampaignFiles(string campaignId)
    {
        return context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerType == StoredFileOwnerType.Campaign
                && file.OwnerId == campaignId
                && file.StorageState == StorageObjectState.Active
                && file.DeletedAtUtc == null
                && file.SupersededByFileId == null);
    }
}
