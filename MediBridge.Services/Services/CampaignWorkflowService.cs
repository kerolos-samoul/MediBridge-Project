using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class CampaignWorkflowService : ICampaignWorkflowService
{
    private const decimal DefaultPlatformFeePercent = 20m;
    // QueueItemStatus intentionally has no Expired state; delivered-message expiry belongs to another workflow.
    private const int ExpiredQueueCount = 0;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IValidator<CreateCampaignDraftRequestDto> draftValidator;
    private readonly IValidator<CampaignAssetUploadRequestDto> assetValidator;
    private readonly IFileStorageProvider fileStorageProvider;
    private readonly IAuditLogger auditLogger;
    private readonly ICurrentUserContext currentUserContext;

    public CampaignWorkflowService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IValidator<CreateCampaignDraftRequestDto> draftValidator,
        IValidator<CampaignAssetUploadRequestDto> assetValidator,
        IFileStorageProvider fileStorageProvider,
        IAuditLogger auditLogger,
        ICurrentUserContext currentUserContext)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.draftValidator = draftValidator;
        this.assetValidator = assetValidator;
        this.fileStorageProvider = fileStorageProvider;
        this.auditLogger = auditLogger;
        this.currentUserContext = currentUserContext;
    }

    public async Task<CampaignDto> CreateDraftAsync(string companyUserId, CreateCampaignDraftRequestDto request, CancellationToken cancellationToken = default)
    {
        var validation = await draftValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        return await domainUnitOfWork.ExecuteInTransactionAsync<CampaignDto>(async transactionCancellationToken =>
        {
            var company = await GetApprovedCompanyForUpdateAsync(companyUserId, transactionCancellationToken);
            var campaign = new Campaign
            {
                CompanyId = company.Id,
                Title = request.Title.Trim(),
                Description = request.Description.Trim(),
                ClinicalResearchInfo = NormalizeOptionalText(request.ClinicalResearchInfo),
                Status = CampaignStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow
            };

            await domainUnitOfWork.Campaigns.AddCampaignAsync(campaign, transactionCancellationToken);
            return ToCampaignDto(campaign);
        }, cancellationToken);
    }

    public async Task<CampaignAssetDto> UploadAssetAsync(
        string companyUserId,
        string campaignId,
        CampaignAssetUploadRequestDto request,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var validation = await assetValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        var company = await GetApprovedCompanyAsync(companyUserId, cancellationToken);
        var campaign = await GetOwnedCampaignAsync(company.Id, campaignId, cancellationToken);
        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new WorkflowConflictException("Campaign assets can only be uploaded while the campaign is a draft.");
        }

        var upload = await fileStorageProvider.UploadAsync(
            new FileStorageUpload(
                $"campaigns/{campaignId}",
                request.OriginalFileName,
                request.ContentType,
                request.SizeBytes),
            content,
            cancellationToken);

        try
        {
            return await domainUnitOfWork.ExecuteInTransactionAsync<CampaignAssetDto>(async transactionCancellationToken =>
            {
                var lockedCompany = await GetApprovedCompanyForUpdateAsync(companyUserId, transactionCancellationToken);
                var lockedCampaign = await GetOwnedCampaignForUpdateAsync(lockedCompany.Id, campaignId, transactionCancellationToken);
                if (lockedCampaign.Status != CampaignStatus.Draft)
                {
                    throw new WorkflowConflictException("Campaign assets can only be uploaded while the campaign is a draft.");
                }

                var asset = new StoredFile
                {
                    OwnerType = StoredFileOwnerType.Campaign,
                    OwnerId = lockedCampaign.Id,
                    Purpose = StoredFilePurpose.CampaignMedia,
                    OriginalFileName = request.OriginalFileName.Trim(),
                    ContentType = request.ContentType.Trim(),
                    SizeBytes = request.SizeBytes,
                    StorageKey = upload.StorageKey,
                    StorageResourceType = upload.ResourceType,
                    Visibility = StoredFileVisibility.Private,
                    ReviewStatus = StoredFileReviewStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await domainUnitOfWork.StoredFiles.AddStoredFileAsync(asset, transactionCancellationToken);
                return ToCampaignAssetDto(asset);
            }, cancellationToken);
        }
        catch
        {
            try
            {
                await fileStorageProvider.DeleteAsync(upload.StorageKey, upload.ResourceType, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                await RecordStorageCleanupFailureAsync(upload.StorageKey, cleanupException, CancellationToken.None);
            }

            throw;
        }
    }

    public async Task<CampaignAssetDto> ReplaceAssetAsync(
        string companyUserId,
        string campaignId,
        string assetId,
        CampaignAssetUploadRequestDto request,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var validation = await assetValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        var company = await GetApprovedCompanyAsync(companyUserId, cancellationToken);
        var campaign = await GetOwnedCampaignAsync(company.Id, campaignId, cancellationToken);
        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new WorkflowConflictException("Campaign assets can only be replaced while the campaign is a draft.");
        }

        var existingAsset = await domainUnitOfWork.StoredFiles.FindStoredFileAsync(assetId, cancellationToken)
            ?? throw new WorkflowNotFoundException("Not found.");
        EnsureReplaceableAsset(existingAsset, campaign.Id);

        var upload = await fileStorageProvider.UploadAsync(
            new FileStorageUpload(
                $"campaigns/{campaignId}",
                request.OriginalFileName,
                request.ContentType,
                request.SizeBytes),
            content,
            cancellationToken);

        try
        {
            return await domainUnitOfWork.ExecuteInTransactionAsync<CampaignAssetDto>(async transactionCancellationToken =>
            {
                var lockedCompany = await GetApprovedCompanyForUpdateAsync(companyUserId, transactionCancellationToken);
                var lockedCampaign = await GetOwnedCampaignForUpdateAsync(lockedCompany.Id, campaignId, transactionCancellationToken);
                if (lockedCampaign.Status != CampaignStatus.Draft)
                {
                    throw new WorkflowConflictException("Campaign assets can only be replaced while the campaign is a draft.");
                }

                var lockedAsset = await domainUnitOfWork.StoredFiles.FindStoredFileForUpdateAsync(assetId, transactionCancellationToken)
                    ?? throw new WorkflowNotFoundException("Not found.");
                EnsureReplaceableAsset(lockedAsset, lockedCampaign.Id);

                var replacement = new StoredFile
                {
                    OwnerType = StoredFileOwnerType.Campaign,
                    OwnerId = lockedCampaign.Id,
                    Purpose = StoredFilePurpose.CampaignMedia,
                    OriginalFileName = request.OriginalFileName.Trim(),
                    ContentType = request.ContentType.Trim(),
                    SizeBytes = request.SizeBytes,
                    StorageKey = upload.StorageKey,
                    StorageResourceType = upload.ResourceType,
                    Visibility = StoredFileVisibility.Private,
                    ReviewStatus = StoredFileReviewStatus.Pending,
                    StorageState = StorageObjectState.Active,
                    CreatedAtUtc = DateTime.UtcNow
                };

                lockedAsset.SupersededByFileId = replacement.Id;
                await domainUnitOfWork.StoredFiles.AddStoredFileAsync(replacement, transactionCancellationToken);
                return ToCampaignAssetDto(replacement);
            }, cancellationToken);
        }
        catch
        {
            try
            {
                await fileStorageProvider.DeleteAsync(upload.StorageKey, upload.ResourceType, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                await RecordStorageCleanupFailureAsync(upload.StorageKey, cleanupException, CancellationToken.None);
            }

            throw;
        }
    }

    public async Task DeleteAssetAsync(
        string companyUserId,
        string campaignId,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        var deletion = await domainUnitOfWork.ExecuteInTransactionAsync<AssetDeletionRequest?>(async transactionCancellationToken =>
        {
            var company = await GetApprovedCompanyForUpdateAsync(companyUserId, transactionCancellationToken);
            var campaign = await GetOwnedCampaignForUpdateAsync(company.Id, campaignId, transactionCancellationToken);
            if (campaign.Status != CampaignStatus.Draft)
            {
                throw new WorkflowConflictException("Campaign assets can only be deleted while the campaign is a draft.");
            }

            var asset = await domainUnitOfWork.StoredFiles.FindStoredFileForUpdateAsync(assetId, transactionCancellationToken)
                ?? throw new WorkflowNotFoundException("Not found.");

            if (asset.StorageState == StorageObjectState.Deleted)
            {
                return null;
            }

            EnsureDeletableAsset(asset, campaign.Id);
            asset.StorageState = StorageObjectState.DeletionPending;
            return new AssetDeletionRequest(asset.Id, asset.StorageKey, asset.StorageResourceType);
        }, cancellationToken);

        if (deletion is null)
        {
            return;
        }

        try
        {
            await fileStorageProvider.DeleteAsync(deletion.StorageKey, deletion.ResourceType, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new FileStorageUnavailableException("File storage provider is unavailable.", exception);
        }

        await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var asset = await domainUnitOfWork.StoredFiles.FindStoredFileForUpdateAsync(deletion.AssetId, transactionCancellationToken)
                ?? throw new WorkflowNotFoundException("Not found.");
            if (asset.StorageState != StorageObjectState.Deleted)
            {
                asset.StorageState = StorageObjectState.Deleted;
                asset.DeletedAtUtc = DateTime.UtcNow;
            }
        }, cancellationToken);
    }

    public async Task<TargetPreviewDto> PreviewTargetsAsync(string companyUserId, string campaignId, CancellationToken cancellationToken = default)
    {
        var company = await GetApprovedCompanyAsync(companyUserId, cancellationToken);
        await GetOwnedCampaignAsync(company.Id, campaignId, cancellationToken);
        var eligibleDoctors = await ResolveEligibleDoctorsAsync(cancellationToken);
        var platformFeePercent = await GetActivePlatformFeePercentAsync(cancellationToken);
        var estimatedTotalCost = CalculateEstimatedTotalCost(eligibleDoctors, platformFeePercent);

        return new TargetPreviewDto(eligibleDoctors.Count, estimatedTotalCost, "EGP");
    }

    public async Task<CampaignSubmissionDto> SubmitCampaignAsync(string companyUserId, string campaignId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var safeIdempotencyKey = ValidateIdempotencyKey(idempotencyKey);

        return await domainUnitOfWork.ExecuteInTransactionAsync<CampaignSubmissionDto>(async transactionCancellationToken =>
        {
            var company = await GetApprovedCompanyForUpdateAsync(companyUserId, transactionCancellationToken);
            var campaign = await GetOwnedCampaignForUpdateAsync(company.Id, campaignId, transactionCancellationToken);
            var financialKey = CreateCampaignFinancialIdempotencyKey(company.Id, campaign.Id, safeIdempotencyKey);
            var reserveAlreadyExists = await domainUnitOfWork.WalletTransactions.IdempotencyKeyExistsAsync(
                WalletTransactionType.Reserve,
                financialKey,
                transactionCancellationToken);
            if (reserveAlreadyExists && campaign.Status == CampaignStatus.PendingReview)
            {
                var existingTargets = await domainUnitOfWork.Campaigns.ListCampaignTargetsAsync(campaign.Id, transactionCancellationToken);
                return new CampaignSubmissionDto(campaign.Id, campaign.Status.ToString(), existingTargets.Count, existingTargets.Sum(target => target.PricePerMessageSnapshot));
            }

            if (reserveAlreadyExists)
            {
                throw new WorkflowConflictException("Idempotency conflict.");
            }

            if (campaign.Status != CampaignStatus.Draft)
            {
                throw new WorkflowConflictException("Campaign is not in a submit-ready state.");
            }

            var hasApprovedAsset = await domainUnitOfWork.StoredFiles.HasStoredFileAsync(
                StoredFileOwnerType.Campaign,
                campaign.Id,
                StoredFilePurpose.CampaignMedia,
                StoredFileReviewStatus.Approved,
                transactionCancellationToken);
            if (!hasApprovedAsset)
            {
                throw new WorkflowValidationException("At least one campaign asset must be approved before submission.");
            }

            var eligibleDoctors = await ResolveEligibleDoctorsAsync(transactionCancellationToken);
            if (eligibleDoctors.Count == 0)
            {
                throw new WorkflowValidationException("At least one eligible priced doctor is required before submission.");
            }

            var targetSnapshots = eligibleDoctors
                .Select(doctor => CampaignTargetEligibility.CreateSnapshot(campaign.Id, doctor))
                .ToArray();
            await domainUnitOfWork.Campaigns.ReplaceCampaignTargetsAsync(campaign.Id, targetSnapshots, transactionCancellationToken);

            var platformFeePercent = await GetActivePlatformFeePercentAsync(transactionCancellationToken);
            var reservedAmount = CalculateEstimatedTotalCost(eligibleDoctors, platformFeePercent);
            var wallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateAsync(WalletOwnerType.Company, company.Id, transactionCancellationToken)
                ?? throw new WorkflowValidationException("The company wallet must be funded before submission.");
            if (wallet.AvailableBalance < reservedAmount)
            {
                throw new WorkflowValidationException("The company wallet has insufficient available funds.");
            }

            var transactionId = Guid.NewGuid().ToString("N");
            await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(wallet.Id, -reservedAmount, transactionCancellationToken);
            await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(wallet.Id, reservedAmount, transactionCancellationToken);
            await domainUnitOfWork.WalletTransactions.AddTransactionAsync(
                transactionId,
                wallet.Id,
                WalletTransactionType.Reserve,
                financialKey,
                reservedAmount,
                transactionCancellationToken);
            await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
            {
                WalletTransactionId = transactionId,
                WalletId = wallet.Id,
                Direction = WalletLedgerEntryDirection.Debit,
                BalanceType = WalletBalanceType.Available,
                Amount = reservedAmount,
                CampaignId = campaign.Id,
                CompanyId = company.Id,
                IdempotencyKey = financialKey
            }, transactionCancellationToken);
            await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
            {
                WalletTransactionId = transactionId,
                WalletId = wallet.Id,
                Direction = WalletLedgerEntryDirection.Credit,
                BalanceType = WalletBalanceType.Reserved,
                Amount = reservedAmount,
                CampaignId = campaign.Id,
                CompanyId = company.Id,
                IdempotencyKey = financialKey
            }, transactionCancellationToken);

            var submittedAtUtc = DateTime.UtcNow;
            campaign.Status = CampaignStatus.PendingReview;
            campaign.UpdatedAtUtc = submittedAtUtc;
            await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "CampaignSubmitted",
                AuditOutcome.Success,
                submittedAtUtc,
                "Campaign submitted and company funds reserved.",
                targetType: AuditTargetType.Campaign,
                targetId: campaign.Id,
                cancellationToken: transactionCancellationToken);

            return new CampaignSubmissionDto(campaign.Id, campaign.Status.ToString(), eligibleDoctors.Count, reservedAmount);
        }, cancellationToken);
    }

    public async Task<QueueSummaryDto> GetQueueSummaryAsync(
        string companyUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        var company = await GetApprovedCompanyAsync(companyUserId, cancellationToken);
        var campaign = await GetOwnedCampaignAsync(company.Id, campaignId, cancellationToken);
        var counts = await domainUnitOfWork.MessageQueues.GetQueueItemCountsByCampaignAsync(campaign.Id, cancellationToken);

        return new QueueSummaryDto(
            campaign.Id,
            counts.GetValueOrDefault(QueueItemStatus.Queued),
            counts.GetValueOrDefault(QueueItemStatus.Activated),
            counts.GetValueOrDefault(QueueItemStatus.Cancelled),
            ExpiredQueueCount,
            DateTime.UtcNow);
    }

    private Task<Core.Entities.Profiles.CompanyProfile> GetApprovedCompanyAsync(
        string companyUserId,
        CancellationToken cancellationToken)
        => ResolveApprovedCompanyAsync(companyUserId, useUpdateLock: false, cancellationToken);

    private Task<Core.Entities.Profiles.CompanyProfile> GetApprovedCompanyForUpdateAsync(
        string companyUserId,
        CancellationToken cancellationToken)
        => ResolveApprovedCompanyAsync(companyUserId, useUpdateLock: true, cancellationToken);

    private async Task<Core.Entities.Profiles.CompanyProfile> ResolveApprovedCompanyAsync(
        string companyUserId,
        bool useUpdateLock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            throw new WorkflowUnauthorizedException("Authentication denied.");
        }

        var user = useUpdateLock
            ? await identityUnitOfWork.Users.FindByIdForUpdateAsync(companyUserId, cancellationToken)
            : await identityUnitOfWork.Users.FindByIdAsync(companyUserId, cancellationToken);
        if (user is null)
        {
            throw new WorkflowUnauthorizedException("Authentication denied.");
        }

        if (user.Role != UserRole.Company || user.AccountStatus != AccountStatus.Approved || user.IsDeleted)
        {
            throw new WorkflowForbiddenException("Forbidden.");
        }

        var company = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(companyUserId, cancellationToken);
        if (company is null || company.IsDeleted)
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        return company;
    }

    private async Task<Campaign> GetOwnedCampaignAsync(string companyId, string campaignId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        var campaign = await domainUnitOfWork.Campaigns.FindActiveCampaignAsync(campaignId, cancellationToken)
            ?? throw new WorkflowNotFoundException("Not found.");
        if (!string.Equals(campaign.CompanyId, companyId, StringComparison.Ordinal))
        {
            throw new WorkflowForbiddenException("Forbidden.");
        }

        return campaign;
    }

    private async Task<Campaign> GetOwnedCampaignForUpdateAsync(string companyId, string campaignId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        var campaign = await domainUnitOfWork.Campaigns.FindActiveCampaignForUpdateAsync(campaignId, cancellationToken)
            ?? throw new WorkflowNotFoundException("Not found.");
        if (!string.Equals(campaign.CompanyId, companyId, StringComparison.Ordinal))
        {
            throw new WorkflowForbiddenException("Forbidden.");
        }

        return campaign;
    }

    private async Task<IReadOnlyList<DoctorProfile>> ResolveEligibleDoctorsAsync(CancellationToken cancellationToken)
    {
        var profiles = await identityUnitOfWork.Profiles.ListDoctorProfilesAsync(cancellationToken);
        var eligibleDoctors = new List<DoctorProfile>();
        foreach (var profile in profiles)
        {
            if (profile.IsDeleted
                || profile.Status != DoctorMarketplaceStatus.Active
                || profile.PricePerMessage is not > 0m
                || !MoneyRules.HasTwoOrFewerDecimalPlaces(profile.PricePerMessage.Value))
            {
                continue;
            }

            var user = await identityUnitOfWork.Users.FindByIdAsync(profile.UserId, cancellationToken);
            if (user is not null && CampaignTargetEligibility.IsEligible(profile, user))
            {
                eligibleDoctors.Add(profile);
            }
        }

        return eligibleDoctors;
    }

    private async Task<decimal> GetActivePlatformFeePercentAsync(CancellationToken cancellationToken)
    {
        var feePercent = await domainUnitOfWork.PolicyHistory.FindActivePlatformFeePercentAsync(DateTime.UtcNow, cancellationToken)
            ?? DefaultPlatformFeePercent;
        if (feePercent is < 0m or > 100m)
        {
            throw new WorkflowConflictException("The active platform fee policy is invalid.");
        }

        return feePercent;
    }

    private static decimal CalculateEstimatedTotalCost(IReadOnlyList<DoctorProfile> doctors, decimal platformFeePercent)
    {
        var total = 0m;
        foreach (var doctor in doctors)
        {
            var price = doctor.PricePerMessage!.Value;
            var platformFee = decimal.Round(price * platformFeePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var doctorEarnings = price - platformFee;
            total += platformFee + doctorEarnings;
        }

        return MoneyRules.EnsureValid(total, nameof(total));
    }

    private static void EnsureReplaceableAsset(StoredFile asset, string campaignId)
    {
        EnsureCampaignAsset(asset, campaignId);
        if (asset.StorageState != StorageObjectState.Active
            || asset.DeletedAtUtc is not null
            || !string.IsNullOrWhiteSpace(asset.SupersededByFileId))
        {
            throw new WorkflowConflictException("Campaign asset is not replaceable.");
        }

        if (asset.ReviewStatus is not (StoredFileReviewStatus.Pending or StoredFileReviewStatus.Rejected))
        {
            throw new WorkflowConflictException("Approved campaign assets are immutable.");
        }
    }

    private static void EnsureDeletableAsset(StoredFile asset, string campaignId)
    {
        EnsureCampaignAsset(asset, campaignId);
        if (asset.StorageState == StorageObjectState.DeletionPending)
        {
            return;
        }

        if (asset.StorageState != StorageObjectState.Active
            || asset.DeletedAtUtc is not null
            || !string.IsNullOrWhiteSpace(asset.SupersededByFileId))
        {
            throw new WorkflowConflictException("Campaign asset is not deletable.");
        }

        if (asset.ReviewStatus is not (StoredFileReviewStatus.Pending or StoredFileReviewStatus.Rejected))
        {
            throw new WorkflowConflictException("Approved campaign assets are immutable.");
        }
    }

    private static void EnsureCampaignAsset(StoredFile asset, string campaignId)
    {
        if (asset.OwnerType != StoredFileOwnerType.Campaign
            || asset.Purpose != StoredFilePurpose.CampaignMedia
            || !string.Equals(asset.OwnerId, campaignId, StringComparison.Ordinal))
        {
            throw new WorkflowNotFoundException("Not found.");
        }
    }

    private static string ValidateIdempotencyKey(string idempotencyKey)
    {
        var safeKey = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(safeKey) || safeKey.Length < 8 || safeKey.Length > 128)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        return safeKey;
    }

    private static string CreateCampaignFinancialIdempotencyKey(string companyId, string campaignId, string idempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"campaign-reserve:{companyId}:{campaignId}:{idempotencyKey}"));
        return Convert.ToHexString(bytes);
    }

    private Task RecordStorageCleanupFailureAsync(string storageKey, Exception exception, CancellationToken cancellationToken)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storageKey)));

        return auditLogger.LogAsync(new AuditEvent(
            AuditEventCategory.System,
            $"FileStorageCompensationFailed:{exception.GetType().Name}",
            currentUserContext.UserId,
            currentUserContext.Role,
            "StoredFileObject",
            fingerprint,
            currentUserContext.CorrelationId,
            DateTime.UtcNow), cancellationToken);
    }

    private static CampaignDto ToCampaignDto(Campaign campaign)
        => new(campaign.Id, campaign.CompanyId, campaign.Title, campaign.Description, campaign.Status.ToString());

    private static CampaignAssetDto ToCampaignAssetDto(StoredFile asset)
        => new(asset.Id, asset.OwnerId, asset.ReviewStatus.ToString(), asset.ReviewReason);

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AssetDeletionRequest(string AssetId, string StorageKey, string ResourceType);
}
