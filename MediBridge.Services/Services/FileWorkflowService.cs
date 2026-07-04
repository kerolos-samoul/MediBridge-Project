using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Files;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Files;
using Microsoft.Extensions.Logging;

namespace MediBridge.Services.Services;

public sealed class FileWorkflowService : IFileWorkflowService
{
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IFileStorageProvider storageProvider;
    private readonly FileUploadRequestValidator uploadValidator;
    private readonly FileReviewRequestValidator reviewValidator;
    private readonly ILogger<FileWorkflowService> logger;
    private readonly IEgyptBusinessClock businessClock;

    public FileWorkflowService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IFileStorageProvider storageProvider,
        FileUploadRequestValidator uploadValidator,
        FileReviewRequestValidator reviewValidator,
        ILogger<FileWorkflowService> logger,
        IEgyptBusinessClock businessClock)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.storageProvider = storageProvider;
        this.uploadValidator = uploadValidator;
        this.reviewValidator = reviewValidator;
        this.logger = logger;
        this.businessClock = businessClock;
    }

    public async Task<FileDto> UploadVerificationDocumentAsync(string actorUserId, UserRole role, FileWorkflowUpload upload, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "TEMP verification upload service start. ActorUserId: {ActorUserId}, Role: {Role}, FileName: {FileName}, ContentType: {ContentType}, Length: {Length}",
            actorUserId,
            role,
            upload.FileName,
            upload.ContentType,
            upload.Length);

        var (ownerType, ownerId) = await ResolveVerificationOwnerAsync(actorUserId, role, cancellationToken);

        var validationErrors = uploadValidator.Validate(StoredFilePurpose.VerificationDocument, upload);
        if (validationErrors.Count > 0)
        {
            throw new ValidationException("Validation failed.");
        }

        var storedFileId = Guid.NewGuid().ToString("N");
        var storageKey = CreateStorageKey(ownerType, ownerId, storedFileId, upload.FileName);
        var resourceType = FileUploadRequestValidator.ResolveResourceType(upload.ContentType);
        var now = DateTime.UtcNow;
        var storedFile = new StoredFile
        {
            Id = storedFileId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            Purpose = StoredFilePurpose.VerificationDocument,
            OriginalFileName = Path.GetFileName(upload.FileName),
            ContentType = upload.ContentType,
            SizeBytes = upload.Length,
            StorageKey = storageKey,
            StorageProvider = "Pending",
            StorageResourceType = resourceType,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Pending,
            UploadStatus = StoredFileUploadStatus.PendingUpload,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = now
        };

        await domainUnitOfWork.StoredFiles.AddPendingUploadAsync(storedFile, cancellationToken);
        await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            "FileVerificationUploadPending",
            AuditOutcome.Info,
            now,
            CreateUploadAuditMetadata(actorUserId, role, storedFileId, ownerType, ownerId),
            targetType: AuditTargetType.StoredFile,
            targetId: storedFileId,
            cancellationToken: cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Verification upload pending metadata saved. StoredFileId: {StoredFileId}, OwnerType: {OwnerType}, ResourceType: {ResourceType}",
            storedFileId,
            ownerType,
            resourceType);

        try
        {
            var uploadResponse = await storageProvider.UploadAsync(new FileStorageUploadRequest(
                storageKey,
                upload.Content,
                upload.ContentType,
                Path.GetExtension(upload.FileName).ToLowerInvariant(),
                resourceType,
                StoredFileStorageDeliveryType.Private,
                new Dictionary<string, string>
                {
                    ["purpose"] = StoredFilePurpose.VerificationDocument.ToString(),
                    ["ownerType"] = ownerType.ToString(),
                    ["storedFileId"] = storedFileId
                }),
                cancellationToken);

            logger.LogInformation(
                "Verification upload provider returned. StoredFileId: {StoredFileId}, SizeBytes: {SizeBytes}",
                storedFileId,
                uploadResponse.SizeBytes);

            await domainUnitOfWork.StoredFiles.MarkUploadStoredAsync(
                storedFileId,
                uploadResponse.StorageProvider,
                uploadResponse.StorageKey,
                uploadResponse.ResourceType,
                uploadResponse.DeliveryType,
                uploadResponse.SizeBytes,
                uploadResponse.ContentType,
                cancellationToken);
            await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "FileVerificationUploadStored",
                AuditOutcome.Success,
                DateTime.UtcNow,
                CreateUploadAuditMetadata(actorUserId, role, storedFileId, ownerType, ownerId),
                targetType: AuditTargetType.StoredFile,
                targetId: storedFileId,
                cancellationToken: cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation("TEMP verification upload stored metadata saved. StoredFileId: {StoredFileId}", storedFileId);
        }
        catch (Exception)
        {
            logger.LogError("Verification upload failed after pending metadata. StoredFileId: {StoredFileId}", storedFileId);
            await domainUnitOfWork.StoredFiles.MarkUploadFailedAsync(storedFileId, cancellationToken);
            await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "FileVerificationUploadFailed",
                AuditOutcome.Denied,
                DateTime.UtcNow,
                CreateUploadAuditMetadata(actorUserId, role, storedFileId, ownerType, ownerId),
                targetType: AuditTargetType.StoredFile,
                targetId: storedFileId,
                cancellationToken: cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }

        var completedFile = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new InvalidOperationException("Stored file metadata was not found after upload.");
        return ToFileDto(completedFile);
    }

    public async Task<FileDto> UploadCampaignFileAsync(string actorUserId, string campaignId, StoredFilePurpose purpose, FileWorkflowUpload upload, CancellationToken cancellationToken = default)
    {
        if (purpose is not (StoredFilePurpose.CampaignMedia or StoredFilePurpose.VoiceNote or StoredFilePurpose.ClinicalResearchAttachment))
        {
            throw new ValidationException("Validation failed.");
        }

        var companyId = await ResolveCampaignOwnerAsync(actorUserId, campaignId, cancellationToken);
        var validationErrors = uploadValidator.Validate(purpose, upload);
        if (validationErrors.Count > 0)
        {
            throw new ValidationException("Validation failed.");
        }

        var storedFileId = Guid.NewGuid().ToString("N");
        var storageKey = CreateCampaignStorageKey(companyId, campaignId, storedFileId, upload.FileName);
        var resourceType = FileUploadRequestValidator.ResolveResourceType(upload.ContentType);
        var now = DateTime.UtcNow;
        var storedFile = new StoredFile
        {
            Id = storedFileId,
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = companyId,
            RelatedCampaignId = campaignId,
            Purpose = purpose,
            OriginalFileName = Path.GetFileName(upload.FileName),
            ContentType = upload.ContentType,
            SizeBytes = upload.Length,
            StorageKey = storageKey,
            StorageProvider = "Pending",
            StorageResourceType = resourceType,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Pending,
            UploadStatus = StoredFileUploadStatus.PendingUpload,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = now
        };

        await domainUnitOfWork.StoredFiles.AddPendingUploadAsync(storedFile, cancellationToken);
        await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            "FileCampaignUploadPending",
            AuditOutcome.Info,
            now,
            CreateUploadAuditMetadata(actorUserId, UserRole.Company, storedFileId, StoredFileOwnerType.Company, companyId, purpose, campaignId),
            targetType: AuditTargetType.StoredFile,
            targetId: storedFileId,
            cancellationToken: cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var uploadResponse = await storageProvider.UploadAsync(new FileStorageUploadRequest(
                storageKey,
                upload.Content,
                upload.ContentType,
                Path.GetExtension(upload.FileName).ToLowerInvariant(),
                resourceType,
                StoredFileStorageDeliveryType.Private,
                new Dictionary<string, string>
                {
                    ["purpose"] = purpose.ToString(),
                    ["ownerType"] = StoredFileOwnerType.Company.ToString(),
                    ["storedFileId"] = storedFileId,
                    ["campaignId"] = campaignId
                }),
                cancellationToken);

            await domainUnitOfWork.StoredFiles.MarkUploadStoredAsync(
                storedFileId,
                uploadResponse.StorageProvider,
                uploadResponse.StorageKey,
                uploadResponse.ResourceType,
                uploadResponse.DeliveryType,
                uploadResponse.SizeBytes,
                uploadResponse.ContentType,
                cancellationToken);
            await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "FileCampaignUploadStored",
                AuditOutcome.Success,
                DateTime.UtcNow,
                CreateUploadAuditMetadata(actorUserId, UserRole.Company, storedFileId, StoredFileOwnerType.Company, companyId, purpose, campaignId),
                targetType: AuditTargetType.StoredFile,
                targetId: storedFileId,
                cancellationToken: cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await domainUnitOfWork.StoredFiles.MarkUploadFailedAsync(storedFileId, cancellationToken);
            await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "FileCampaignUploadFailed",
                AuditOutcome.Denied,
                DateTime.UtcNow,
                CreateUploadAuditMetadata(actorUserId, UserRole.Company, storedFileId, StoredFileOwnerType.Company, companyId, purpose, campaignId),
                targetType: AuditTargetType.StoredFile,
                targetId: storedFileId,
                cancellationToken: cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }

        var completedFile = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new InvalidOperationException("Stored file metadata was not found after upload.");
        return ToFileDto(completedFile);
    }

    public Task<FileAccessGrantDto> CreatePrivateAccessGrantAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken = default)
    {
        return CreatePrivateAccessGrantCoreAsync(actorUserId, role, storedFileId, cancellationToken);
    }

    public async Task<DeliveryAssetAccessGrantDto> CreateDeliveryAssetAccessGrantAsync(
        string actorUserId,
        string deliveryId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var doctor = await identityUnitOfWork.Profiles.FindDoctorProfileByUserIdAsync(actorUserId, cancellationToken);
        if (user is null
            || doctor is null
            || user.Role != UserRole.Doctor
            || user.AccountStatus != AccountStatus.Approved
            || user.IsDeleted
            || doctor.IsDeleted
            || doctor.Status != DoctorMarketplaceStatus.Active)
        {
            throw new Phase7ForbiddenException("Doctor access is required.");
        }

        var snapshot = businessClock.Capture();
        var authorization = await domainUnitOfWork.Deliveries.FindDeliveryAssetAuthorizationAsync(
            doctor.Id,
            deliveryId,
            fileId,
            snapshot.BusinessDateEgypt,
            cancellationToken);
        if (authorization is null)
        {
            throw new Phase7NotFoundException("Delivery asset was not found.");
        }

        var expiresAtUtc = snapshot.UtcNow.AddMinutes(10);
        FileStorageAccessGrant grant;
        try
        {
            grant = await storageProvider.CreatePrivateAccessGrantAsync(
                authorization.StorageKey,
                authorization.StorageResourceType,
                expiresAtUtc,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new Phase7StorageUnavailableException("Storage provider is unavailable.", exception);
        }

        if (grant.ExpiresAtUtc <= snapshot.UtcNow)
        {
            throw new Phase7StorageUnavailableException(
                "Storage provider is unavailable.",
                new InvalidOperationException("The storage provider returned an expired grant."));
        }

        await domainUnitOfWork.FileAccessGrantAudits.AddAsync(new FileAccessGrantAudit
        {
            Id = Guid.NewGuid().ToString("N"),
            StoredFileId = fileId,
            RequestedByUserId = actorUserId,
            RequesterRole = UserRole.Doctor.ToString(),
            Outcome = FileAccessGrantOutcome.Issued,
            Reason = "Delivery asset access grant issued.",
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = snapshot.UtcNow
        }, cancellationToken);
        await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            "DeliveryAssetAccessIssued",
            AuditOutcome.Success,
            snapshot.UtcNow,
            JsonSerializer.Serialize(new { actorUserId, deliveryId, fileId, expiresAtUtc }),
            targetType: AuditTargetType.StoredFile,
            targetId: fileId,
            cancellationToken: cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);

        return new DeliveryAssetAccessGrantDto(grant.Url, expiresAtUtc);
    }

    public async Task<FileDto> ReplaceCampaignFileAsync(
        string actorUserId,
        string campaignId,
        string storedFileId,
        FileWorkflowUpload upload,
        CancellationToken cancellationToken = default)
    {
        await EnsureCampaignFileRouteMatchAsync(campaignId, storedFileId, cancellationToken);
        return await ReplaceFileAsync(actorUserId, UserRole.Company, storedFileId, upload, cancellationToken);
    }

    public async Task<DeleteFileResultDto> DeleteCampaignFileAsync(
        string actorUserId,
        string campaignId,
        string storedFileId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCampaignFileRouteMatchAsync(campaignId, storedFileId, cancellationToken);
        return await DeleteFileAsync(actorUserId, UserRole.Company, storedFileId, cancellationToken);
    }

    public Task<FileReviewDto> ReviewFileAsync(string adminUserId, string storedFileId, FileReviewRequestDto request, CancellationToken cancellationToken = default)
    {
        return ReviewFileCoreAsync(adminUserId, storedFileId, request, cancellationToken);
    }

    public async Task<FileDto> ReplaceFileAsync(string actorUserId, UserRole role, string storedFileId, FileWorkflowUpload upload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id is required.");
        }

        var file = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new KeyNotFoundException("Stored file was not found.");
        if (!await IsActorAllowedForFileAsync(actorUserId, role, file, allowAdmin: false, cancellationToken))
        {
            throw new UnauthorizedAccessException("File replacement denied.");
        }

        await EnsureCampaignFileEditableAsync(actorUserId, role, file, cancellationToken);

        if (role != UserRole.Admin && !await domainUnitOfWork.StoredFiles.IsOwnerActiveForNormalAccessAsync(file.OwnerType, file.OwnerId, cancellationToken))
        {
            throw new UnauthorizedAccessException("File owner is inactive.");
        }

        if (file.UploadStatus != StoredFileUploadStatus.Stored ||
            file.ReviewStatus is not (StoredFileReviewStatus.Rejected or StoredFileReviewStatus.ReplacementRequested))
        {
            throw new UnauthorizedAccessException("File is not eligible for replacement.");
        }

        var validationErrors = uploadValidator.Validate(file.Purpose, upload);
        if (validationErrors.Count > 0)
        {
            throw new ValidationException("Validation failed.");
        }

        var replacementId = Guid.NewGuid().ToString("N");
        var storageKey = CreateStorageKey(file.OwnerType, file.OwnerId, replacementId, upload.FileName);
        var resourceType = FileUploadRequestValidator.ResolveResourceType(upload.ContentType);
        var now = DateTime.UtcNow;
        var replacement = new StoredFile
        {
            Id = replacementId,
            OwnerType = file.OwnerType,
            OwnerId = file.OwnerId,
            Purpose = file.Purpose,
            RelatedCampaignId = file.RelatedCampaignId,
            OriginalFileName = Path.GetFileName(upload.FileName),
            ContentType = upload.ContentType,
            SizeBytes = upload.Length,
            StorageKey = storageKey,
            StorageProvider = "Pending",
            StorageResourceType = resourceType,
            StorageDeliveryType = file.StorageDeliveryType,
            Visibility = file.Visibility,
            ReviewStatus = StoredFileReviewStatus.Pending,
            UploadStatus = StoredFileUploadStatus.PendingUpload,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = now
        };

        await domainUnitOfWork.StoredFiles.AddPendingUploadAsync(replacement, cancellationToken);
        await AddReplacementAuditAsync(file.Id, replacementId, actorUserId, role, AuditOutcome.Info, "File replacement pending.", cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var uploadResponse = await storageProvider.UploadAsync(new FileStorageUploadRequest(
                storageKey,
                upload.Content,
                upload.ContentType,
                Path.GetExtension(upload.FileName).ToLowerInvariant(),
                resourceType,
                file.StorageDeliveryType,
                new Dictionary<string, string>
                {
                    ["purpose"] = file.Purpose.ToString(),
                    ["ownerType"] = file.OwnerType.ToString(),
                    ["storedFileId"] = replacementId,
                    ["replacesStoredFileId"] = file.Id
                }),
                cancellationToken);

            await domainUnitOfWork.StoredFiles.MarkUploadStoredAsync(
                replacementId,
                uploadResponse.StorageProvider,
                uploadResponse.StorageKey,
                uploadResponse.ResourceType,
                uploadResponse.DeliveryType,
                uploadResponse.SizeBytes,
                uploadResponse.ContentType,
                cancellationToken);
            await domainUnitOfWork.StoredFiles.LinkReplacementAsync(file.Id, replacementId, cancellationToken);
            await AddReplacementAuditAsync(file.Id, replacementId, actorUserId, role, AuditOutcome.Success, "File replacement uploaded.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await domainUnitOfWork.StoredFiles.MarkUploadFailedAsync(replacementId, cancellationToken);
            await AddReplacementAuditAsync(file.Id, replacementId, actorUserId, role, AuditOutcome.Denied, "File replacement upload failed.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }

        var completedFile = await domainUnitOfWork.StoredFiles.FindByIdAsync(replacementId, cancellationToken)
            ?? throw new InvalidOperationException("Replacement file metadata was not found after upload.");
        return ToFileDto(completedFile, replacedFileIdOverride: file.Id);
    }

    public Task<DeleteFileResultDto> DeleteFileAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken = default)
    {
        return DeleteFileCoreAsync(actorUserId, role, storedFileId, cancellationToken);
    }

    public Task<PendingFileReviewPageDto> ListPendingReviewsAsync(string adminUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        return ListPendingReviewsCoreAsync(adminUserId, pageNumber, pageSize, cancellationToken);
    }

    public Task<IReadOnlyList<FileReviewDto>> GetReviewHistoryAsync(string adminUserId, string storedFileId, CancellationToken cancellationToken = default)
    {
        return GetReviewHistoryCoreAsync(adminUserId, storedFileId, cancellationToken);
    }

    private async Task<FileReviewDto> ReviewFileCoreAsync(string adminUserId, string storedFileId, FileReviewRequestDto request, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, cancellationToken);
        var validationErrors = reviewValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            throw new ValidationException("Validation failed.");
        }

        var file = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new KeyNotFoundException("Stored file was not found.");
        if (file.UploadStatus is StoredFileUploadStatus.Deleted or StoredFileUploadStatus.Replaced)
        {
            throw new KeyNotFoundException("Stored file was not found.");
        }

        if (request.Decision == FileReviewDecision.Correction)
        {
            var correctedReview = await domainUnitOfWork.FileReviews.FindByIdAsync(request.CorrectsReviewId!, cancellationToken)
                ?? throw new ValidationException("Validation failed.");
            if (!string.Equals(correctedReview.StoredFileId, storedFileId, StringComparison.Ordinal))
            {
                throw new ValidationException("Validation failed.");
            }
        }

        var now = DateTime.UtcNow;
        var review = new FileReview
        {
            Id = Guid.NewGuid().ToString("N"),
            StoredFileId = storedFileId,
            AdminUserId = adminUserId,
            Decision = request.Decision,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes,
            CorrectsReviewId = string.IsNullOrWhiteSpace(request.CorrectsReviewId) ? null : request.CorrectsReviewId,
            CreatedAtUtc = now
        };

        await domainUnitOfWork.FileReviews.AddReviewAsync(review, cancellationToken);
        var updated = await domainUnitOfWork.StoredFiles.UpdateReviewSummaryAsync(
            storedFileId,
            MapReviewStatus(file.ReviewStatus, request.Decision),
            adminUserId,
            review.Reason,
            now,
            file.ConcurrencyStamp,
            cancellationToken);
        if (!updated)
        {
            throw new InvalidOperationException("File review conflict.");
        }

        await AddReviewAuditAsync(storedFileId, review.Id, adminUserId, request.Decision, cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        return ToFileReviewDto(review);
    }

    private async Task<PendingFileReviewPageDto> ListPendingReviewsCoreAsync(string adminUserId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, cancellationToken);
        var normalizedPageNumber = Math.Max(1, pageNumber);
        var normalizedPageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var skip = (normalizedPageNumber - 1) * normalizedPageSize;
        var files = await domainUnitOfWork.StoredFiles.ListByReviewStatusAsync(StoredFileReviewStatus.Pending, skip, normalizedPageSize, cancellationToken);
        var totalCount = await domainUnitOfWork.StoredFiles.CountByReviewStatusAsync(StoredFileReviewStatus.Pending, cancellationToken);
        return new PendingFileReviewPageDto(files.Select(file => ToFileDto(file)).ToList(), normalizedPageNumber, normalizedPageSize, totalCount);
    }

    private async Task<IReadOnlyList<FileReviewDto>> GetReviewHistoryCoreAsync(string adminUserId, string storedFileId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminUserId, cancellationToken);
        var file = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new KeyNotFoundException("Stored file was not found.");
        var reviews = await domainUnitOfWork.FileReviews.ListByStoredFileAsync(file.Id, cancellationToken);
        return reviews.Select(ToFileReviewDto).ToList();
    }

    private async Task EnsureAdminAsync(string adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new UnauthorizedAccessException("Admin user id is required.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (user is null || user.Role != UserRole.Admin || user.IsDeleted)
        {
            throw new UnauthorizedAccessException("Admin role is required.");
        }
    }

    private async Task<(StoredFileOwnerType OwnerType, string OwnerId)> ResolveVerificationOwnerAsync(string actorUserId, UserRole role, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id is required.");
        }

        return role switch
        {
            UserRole.Doctor => (StoredFileOwnerType.Doctor, (await identityUnitOfWork.Profiles.FindDoctorProfileByUserIdAsync(actorUserId, cancellationToken))?.Id
                ?? throw new UnauthorizedAccessException("Doctor profile is required.")),
            UserRole.Company => (StoredFileOwnerType.Company, (await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(actorUserId, cancellationToken))?.Id
                ?? throw new UnauthorizedAccessException("Company profile is required.")),
            _ => throw new UnauthorizedAccessException("Doctor or Company role is required.")
        };
    }

    private async Task<string> ResolveCampaignOwnerAsync(string actorUserId, string campaignId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id is required.");
        }

        var companyProfile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(actorUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Company profile is required.");
        if (!await domainUnitOfWork.StoredFiles.IsOwnerActiveForNormalAccessAsync(StoredFileOwnerType.Company, companyProfile.Id, cancellationToken))
        {
            throw new UnauthorizedAccessException("Company owner is inactive.");
        }

        var activeCampaignId = await domainUnitOfWork.Campaigns.FindActiveCampaignIdAsync(campaignId, cancellationToken);
        if (activeCampaignId is null)
        {
            throw new KeyNotFoundException("Campaign was not found.");
        }

        if (!await domainUnitOfWork.Campaigns.IsActiveEditableCampaignOwnedByCompanyAsync(campaignId, companyProfile.Id, cancellationToken))
        {
            throw new UnauthorizedAccessException("Campaign file changes require an owned Draft or RevisionRequired campaign.");
        }

        return companyProfile.Id;
    }

    private async Task EnsureCampaignFileEditableAsync(
        string actorUserId,
        UserRole role,
        StoredFile file,
        CancellationToken cancellationToken)
    {
        if (role != UserRole.Company || string.IsNullOrWhiteSpace(file.RelatedCampaignId))
        {
            return;
        }

        var companyId = await ResolveCampaignOwnerAsync(
            actorUserId,
            file.RelatedCampaignId,
            cancellationToken);
        if (file.OwnerType != StoredFileOwnerType.Company
            || !string.Equals(file.OwnerId, companyId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Campaign file changes require ownership.");
        }
    }

    private async Task EnsureCampaignFileRouteMatchAsync(
        string campaignId,
        string storedFileId,
        CancellationToken cancellationToken)
    {
        var file = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new KeyNotFoundException("Stored file was not found.");
        if (!string.Equals(file.RelatedCampaignId, campaignId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Stored file was not found.");
        }
    }

    private async Task<FileAccessGrantDto> CreatePrivateAccessGrantCoreAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id is required.");
        }

        var file = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new KeyNotFoundException("Stored file was not found.");
        if (!IsFileAvailableForPrivateAccess(file))
        {
            await AddAccessDeniedAuditAsync(file, actorUserId, role, FileAccessGrantOutcome.Denied, "File unavailable.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new KeyNotFoundException("Stored file was not found.");
        }

        if (!await IsActorAllowedForFileAsync(actorUserId, role, file, allowAdmin: true, cancellationToken))
        {
            await AddAccessDeniedAuditAsync(file, actorUserId, role, FileAccessGrantOutcome.Denied, "Access denied.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("File access denied.");
        }

        if (role != UserRole.Admin && !await domainUnitOfWork.StoredFiles.IsOwnerActiveForNormalAccessAsync(file.OwnerType, file.OwnerId, cancellationToken))
        {
            await AddAccessDeniedAuditAsync(file, actorUserId, role, FileAccessGrantOutcome.Denied, "Owner inactive.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("File owner is inactive.");
        }

        var requestedAtUtc = DateTime.UtcNow;
        var expiresAtUtc = requestedAtUtc.AddMinutes(10);
        FileStorageAccessGrant grant;
        try
        {
            grant = await storageProvider.CreatePrivateAccessGrantAsync(file.StorageKey, file.StorageResourceType, expiresAtUtc, cancellationToken);
        }
        catch
        {
            throw new InvalidOperationException("Storage provider operation failed.");
        }

        if (grant.ExpiresAtUtc <= requestedAtUtc)
        {
            await AddAccessDeniedAuditAsync(file, actorUserId, role, FileAccessGrantOutcome.Expired, "Access grant expired.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("File access grant expired.");
        }

        await domainUnitOfWork.FileAccessGrantAudits.AddAsync(new FileAccessGrantAudit
        {
            Id = Guid.NewGuid().ToString("N"),
            StoredFileId = file.Id,
            RequestedByUserId = actorUserId,
            RequesterRole = role.ToString(),
            Outcome = FileAccessGrantOutcome.Issued,
            Reason = "Access grant issued.",
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = requestedAtUtc
        }, cancellationToken);
        await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            "FilePrivateAccessIssued",
            AuditOutcome.Success,
            requestedAtUtc,
            CreateAccessAuditMetadata(actorUserId, role, file.Id, FileAccessGrantOutcome.Issued, expiresAtUtc),
            targetType: AuditTargetType.StoredFile,
            targetId: file.Id,
            cancellationToken: cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);

        return new FileAccessGrantDto(grant.Url, expiresAtUtc);
    }

    private async Task<DeleteFileResultDto> DeleteFileCoreAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id is required.");
        }

        var file = await domainUnitOfWork.StoredFiles.FindByIdAsync(storedFileId, cancellationToken)
            ?? throw new KeyNotFoundException("Stored file was not found.");
        if (file.UploadStatus is StoredFileUploadStatus.Deleted or StoredFileUploadStatus.Replaced)
        {
            throw new KeyNotFoundException("Stored file was not found.");
        }

        if (!await IsActorAllowedForFileAsync(actorUserId, role, file, allowAdmin: true, cancellationToken))
        {
            await AddDeleteAuditAsync(file.Id, actorUserId, role, AuditOutcome.Denied, "Access denied.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("File deletion denied.");
        }

        await EnsureCampaignFileEditableAsync(actorUserId, role, file, cancellationToken);

        if (role != UserRole.Admin && !await domainUnitOfWork.StoredFiles.IsOwnerActiveForNormalAccessAsync(file.OwnerType, file.OwnerId, cancellationToken))
        {
            await AddDeleteAuditAsync(file.Id, actorUserId, role, AuditOutcome.Denied, "Owner inactive.", cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("File owner is inactive.");
        }

        try
        {
            await storageProvider.DeleteAsync(file.StorageKey, file.StorageResourceType, cancellationToken);
        }
        catch
        {
            throw new InvalidOperationException("Storage provider operation failed.");
        }

        var deletedAtUtc = DateTime.UtcNow;
        await domainUnitOfWork.StoredFiles.MarkDeletedAsync(file.Id, deletedAtUtc, cancellationToken);
        await AddDeleteAuditAsync(file.Id, actorUserId, role, AuditOutcome.Success, "File deleted.", cancellationToken, deletedAtUtc);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteFileResultDto(file.Id, StoredFileUploadStatus.Deleted, deletedAtUtc);
    }

    private async Task<bool> IsActorAllowedForFileAsync(string actorUserId, UserRole role, StoredFile file, bool allowAdmin, CancellationToken cancellationToken)
    {
        if (allowAdmin && role == UserRole.Admin)
        {
            return true;
        }

        if (role is not (UserRole.Doctor or UserRole.Company))
        {
            return false;
        }

        var (ownerType, ownerId) = await ResolveVerificationOwnerAsync(actorUserId, role, cancellationToken);
        return file.OwnerType == ownerType && string.Equals(file.OwnerId, ownerId, StringComparison.Ordinal);
    }

    private static bool IsFileAvailableForPrivateAccess(StoredFile file)
    {
        return file.UploadStatus == StoredFileUploadStatus.Stored &&
               file.DeletedAtUtc is null &&
               !string.IsNullOrWhiteSpace(file.StorageKey) &&
               file.ReviewStatus is not StoredFileReviewStatus.Rejected and not StoredFileReviewStatus.Quarantined;
    }

    private async Task AddAccessDeniedAuditAsync(StoredFile file, string actorUserId, UserRole role, FileAccessGrantOutcome outcome, string reason, CancellationToken cancellationToken)
    {
        await domainUnitOfWork.FileAccessGrantAudits.AddAsync(new FileAccessGrantAudit
        {
            Id = Guid.NewGuid().ToString("N"),
            StoredFileId = file.Id,
            RequestedByUserId = actorUserId,
            RequesterRole = role.ToString(),
            Outcome = outcome,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken);
        await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            outcome == FileAccessGrantOutcome.Expired ? "FilePrivateAccessExpired" : "FilePrivateAccessDenied",
            AuditOutcome.Denied,
            DateTime.UtcNow,
            CreateAccessAuditMetadata(actorUserId, role, file.Id, outcome, expiresAtUtc: null),
            targetType: AuditTargetType.StoredFile,
            targetId: file.Id,
            cancellationToken: cancellationToken);
    }

    private static string CreateStorageKey(StoredFileOwnerType ownerType, string ownerId, string storedFileId, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        return $"verification/{ownerType.ToString().ToLowerInvariant()}/{ownerId}/{storedFileId}{extension}";
    }

    private static string CreateCampaignStorageKey(string companyId, string campaignId, string storedFileId, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        return $"campaigns/{companyId}/{campaignId}/{storedFileId}{extension}";
    }

    private static string CreateUploadAuditMetadata(
        string actorUserId,
        UserRole role,
        string storedFileId,
        StoredFileOwnerType ownerType,
        string ownerId,
        StoredFilePurpose purpose = StoredFilePurpose.VerificationDocument,
        string? campaignId = null)
    {
        return JsonSerializer.Serialize(new
        {
            ActorUserId = actorUserId,
            ActorRole = role.ToString(),
            StoredFileId = storedFileId,
            OwnerType = ownerType.ToString(),
            OwnerId = ownerId,
            Purpose = purpose.ToString(),
            CampaignId = campaignId
        });
    }

    private static string CreateAccessAuditMetadata(string actorUserId, UserRole role, string storedFileId, FileAccessGrantOutcome outcome, DateTime? expiresAtUtc)
    {
        return JsonSerializer.Serialize(new
        {
            ActorUserId = actorUserId,
            ActorRole = role.ToString(),
            StoredFileId = storedFileId,
            Outcome = outcome.ToString(),
            ExpiresAtUtc = expiresAtUtc
        });
    }

    private Task AddDeleteAuditAsync(string storedFileId, string actorUserId, UserRole role, AuditOutcome outcome, string reason, CancellationToken cancellationToken, DateTime? deletedAtUtc = null)
    {
        return domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            outcome == AuditOutcome.Success ? "FileDeleted" : "FileDeleteDenied",
            outcome,
            DateTime.UtcNow,
            JsonSerializer.Serialize(new
            {
                ActorUserId = actorUserId,
                ActorRole = role.ToString(),
                StoredFileId = storedFileId,
                Reason = reason,
                DeletedAtUtc = deletedAtUtc
            }),
            targetType: AuditTargetType.StoredFile,
            targetId: storedFileId,
            cancellationToken: cancellationToken);
    }

    private Task AddReviewAuditAsync(string storedFileId, string reviewId, string adminUserId, FileReviewDecision decision, CancellationToken cancellationToken)
    {
        return domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            "FileReviewDecisionRecorded",
            AuditOutcome.Success,
            DateTime.UtcNow,
            JsonSerializer.Serialize(new
            {
                StoredFileId = storedFileId,
                ReviewId = reviewId,
                AdminUserId = adminUserId,
                Decision = decision.ToString()
            }),
            targetType: AuditTargetType.StoredFile,
            targetId: storedFileId,
            cancellationToken: cancellationToken);
    }

    private Task AddReplacementAuditAsync(string originalFileId, string replacementFileId, string actorUserId, UserRole role, AuditOutcome outcome, string reason, CancellationToken cancellationToken)
    {
        return domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            outcome == AuditOutcome.Success ? "FileReplacementUploaded" : outcome == AuditOutcome.Info ? "FileReplacementPending" : "FileReplacementFailed",
            outcome,
            DateTime.UtcNow,
            JsonSerializer.Serialize(new
            {
                ActorUserId = actorUserId,
                ActorRole = role.ToString(),
                OriginalFileId = originalFileId,
                ReplacementFileId = replacementFileId,
                Reason = reason
            }),
            targetType: AuditTargetType.StoredFile,
            targetId: originalFileId,
            cancellationToken: cancellationToken);
    }

    private static StoredFileReviewStatus MapReviewStatus(StoredFileReviewStatus currentStatus, FileReviewDecision decision)
    {
        return decision switch
        {
            FileReviewDecision.Approved => StoredFileReviewStatus.Approved,
            FileReviewDecision.Rejected => StoredFileReviewStatus.Rejected,
            FileReviewDecision.Quarantined => StoredFileReviewStatus.Quarantined,
            FileReviewDecision.ReplacementRequested => StoredFileReviewStatus.ReplacementRequested,
            FileReviewDecision.Correction => currentStatus,
            _ => currentStatus
        };
    }

    private static FileReviewDto ToFileReviewDto(FileReview review)
    {
        return new FileReviewDto(
            review.Id,
            review.StoredFileId,
            review.Decision,
            review.Reason,
            review.AdminUserId,
            review.CreatedAtUtc,
            review.CorrectsReviewId);
    }

    private static FileDto ToFileDto(StoredFile file, string? replacedFileIdOverride = null)
    {
        return new FileDto(
            file.Id,
            file.Purpose,
            file.OwnerType,
            file.OwnerId,
            file.RelatedCampaignId,
            replacedFileIdOverride ?? file.ReplacedByFileId,
            file.OriginalFileName,
            file.ContentType,
            file.SizeBytes,
            file.ReviewStatus,
            file.UploadStatus,
            file.SafetyScanStatus,
            file.CreatedAtUtc);
    }
}
