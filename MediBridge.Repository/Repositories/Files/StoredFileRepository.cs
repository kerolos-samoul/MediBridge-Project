using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Identity;
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
            ReviewStatus = StoredFileReviewStatus.Pending,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred
        }, cancellationToken);
    }

    public async Task AddPendingUploadAsync(StoredFile storedFile, CancellationToken cancellationToken = default)
    {
        storedFile.UploadStatus = StoredFileUploadStatus.PendingUpload;
        storedFile.SafetyScanStatus = StoredFileSafetyScanStatus.Deferred;
        storedFile.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await context.StoredFiles.AddAsync(storedFile, cancellationToken);
    }

    public Task<StoredFile?> FindByIdAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return context.StoredFiles.FirstOrDefaultAsync(file => file.Id == storedFileId, cancellationToken);
    }

    public Task<string?> FindActiveStoredFileIdAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return context.StoredFiles
            .Where(file => file.Id == storedFileId && file.UploadStatus == StoredFileUploadStatus.Stored)
            .Select(file => file.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActiveStoredFileIdsByOwnerAsync(StoredFileOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return await context.StoredFiles
            .Where(file => file.OwnerType == ownerType && file.OwnerId == ownerId && file.UploadStatus == StoredFileUploadStatus.Stored)
            .OrderBy(file => file.CreatedAtUtc)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFile>> ListByOwnerAsync(StoredFileOwnerType ownerType, string ownerId, StoredFilePurpose? purpose = null, CancellationToken cancellationToken = default)
    {
        var query = context.StoredFiles.Where(file => file.OwnerType == ownerType && file.OwnerId == ownerId);
        if (purpose is not null)
        {
            query = query.Where(file => file.Purpose == purpose);
        }

        return await query.OrderBy(file => file.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFile>> ListByCampaignAsync(string campaignId, StoredFilePurpose? purpose = null, CancellationToken cancellationToken = default)
    {
        var query = context.StoredFiles.Where(file => file.RelatedCampaignId == campaignId);
        if (purpose is not null)
        {
            query = query.Where(file => file.Purpose == purpose);
        }

        return await query.OrderBy(file => file.CreatedAtUtc).ToListAsync(cancellationToken);
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
            .AsNoTracking()
            .ToListAsync(cancellationToken);
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
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasActiveApprovedCampaignMediaAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return ActiveCampaignFiles(campaignId).AnyAsync(
            file => file.Purpose == StoredFilePurpose.CampaignMedia
                && file.ReviewStatus == StoredFileReviewStatus.Approved,
            cancellationToken);
    }

    private IQueryable<StoredFile> ActiveCampaignFiles(string campaignId)
    {
        return context.StoredFiles.Where(file => file.RelatedCampaignId == campaignId
            && file.UploadStatus == StoredFileUploadStatus.Stored
            && file.DeletedAtUtc == null
            && file.ReplacedByFileId == null);
    }

    public async Task<IReadOnlyList<string>> ListStoredFileReviewIdsAsync(StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default)
    {
        return await context.StoredFiles
            .Where(file => file.ReviewStatus == reviewStatus)
            .OrderBy(file => file.CreatedAtUtc)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFile>> ListByReviewStatusAsync(StoredFileReviewStatus reviewStatus, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        return await context.StoredFiles
            .Where(file => file.ReviewStatus == reviewStatus && file.UploadStatus == StoredFileUploadStatus.Stored)
            .OrderBy(file => file.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountByReviewStatusAsync(StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default)
    {
        return context.StoredFiles.CountAsync(
            file => file.ReviewStatus == reviewStatus && file.UploadStatus == StoredFileUploadStatus.Stored,
            cancellationToken);
    }

    public async Task MarkUploadStoredAsync(string storedFileId, string storageProvider, string storageKey, StoredFileStorageResourceType resourceType, StoredFileStorageDeliveryType deliveryType, long sizeBytes, string contentType, CancellationToken cancellationToken = default)
    {
        var file = await RequireStoredFileAsync(storedFileId, cancellationToken);
        file.StorageProvider = storageProvider;
        file.StorageKey = storageKey;
        file.StorageResourceType = resourceType;
        file.StorageDeliveryType = deliveryType;
        file.SizeBytes = sizeBytes;
        file.ContentType = contentType;
        file.UploadStatus = StoredFileUploadStatus.Stored;
        file.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    public async Task MarkUploadFailedAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        var file = await RequireStoredFileAsync(storedFileId, cancellationToken);
        file.UploadStatus = StoredFileUploadStatus.UploadFailed;
        file.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    public async Task<bool> UpdateReviewSummaryAsync(string storedFileId, StoredFileReviewStatus reviewStatus, string adminUserId, string? reason, DateTime reviewedAtUtc, string expectedConcurrencyStamp, CancellationToken cancellationToken = default)
    {
        var file = await context.StoredFiles.FirstOrDefaultAsync(item => item.Id == storedFileId, cancellationToken);
        if (file is null || file.ConcurrencyStamp != expectedConcurrencyStamp)
        {
            return false;
        }

        file.ReviewStatus = reviewStatus;
        file.ReviewedByAdminId = adminUserId;
        file.ReviewReason = reason;
        file.ReviewedAtUtc = reviewedAtUtc;
        file.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        return true;
    }

    public async Task MarkDeletedAsync(string storedFileId, DateTime deletedAtUtc, CancellationToken cancellationToken = default)
    {
        var file = await RequireStoredFileAsync(storedFileId, cancellationToken);
        file.UploadStatus = StoredFileUploadStatus.Deleted;
        file.DeletedAtUtc = deletedAtUtc;
        file.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    public async Task LinkReplacementAsync(string originalFileId, string replacementFileId, CancellationToken cancellationToken = default)
    {
        var original = await RequireStoredFileAsync(originalFileId, cancellationToken);
        original.ReplacedByFileId = replacementFileId;
        original.UploadStatus = StoredFileUploadStatus.Replaced;
        original.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    public async Task<bool> IsOwnerActiveForNormalAccessAsync(StoredFileOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        if (ownerType == StoredFileOwnerType.Doctor)
        {
            return await (
                from profile in context.DoctorProfiles
                join user in context.Users on profile.UserId equals user.Id
                where profile.Id == ownerId &&
                      !profile.IsDeleted &&
                      profile.Status != DoctorMarketplaceStatus.Suspended &&
                      !user.IsDeleted &&
                      user.AccountStatus == AccountStatus.Approved
                select profile.Id)
                .AnyAsync(cancellationToken);
        }

        if (ownerType == StoredFileOwnerType.Company)
        {
            return await (
                from profile in context.CompanyProfiles
                join user in context.Users on profile.UserId equals user.Id
                where profile.Id == ownerId &&
                      !profile.IsDeleted &&
                      !user.IsDeleted &&
                      user.AccountStatus == AccountStatus.Approved
                select profile.Id)
                .AnyAsync(cancellationToken);
        }

        return ownerType is StoredFileOwnerType.Admin or StoredFileOwnerType.Campaign;
    }

    public Task<bool> IsAvailableAsApprovedAssetAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return context.StoredFiles.AnyAsync(file =>
            file.Id == storedFileId &&
            file.UploadStatus == StoredFileUploadStatus.Stored &&
            file.ReviewStatus == StoredFileReviewStatus.Approved &&
            file.DeletedAtUtc == null &&
            file.ReplacedByFileId == null,
            cancellationToken);
    }

    private async Task<StoredFile> RequireStoredFileAsync(string storedFileId, CancellationToken cancellationToken)
    {
        return await context.StoredFiles.FirstOrDefaultAsync(file => file.Id == storedFileId, cancellationToken)
            ?? throw new InvalidOperationException($"Stored file '{storedFileId}' was not found.");
    }
}
