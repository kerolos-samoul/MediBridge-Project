using FluentValidation;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class AdminCampaignReviewService : IAdminCampaignReviewService
{
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IValidator<ReviewDecisionRequestDto> validator;
    private readonly IFileAccessService fileAccessService;

    public AdminCampaignReviewService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IValidator<ReviewDecisionRequestDto> validator,
        IFileAccessService fileAccessService)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.validator = validator;
        this.fileAccessService = fileAccessService;
    }

    public async Task<PendingCampaignPageDto> ListPendingCampaignsAsync(
        string adminUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await GetApprovedAdminIdAsync(adminUserId, cancellationToken);
        var boundedPageNumber = Math.Max(1, pageNumber);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);
        var skipValue = ((long)boundedPageNumber - 1L) * boundedPageSize;
        var skip = skipValue > int.MaxValue ? int.MaxValue : (int)skipValue;
        var page = await domainUnitOfWork.Campaigns.ListPendingReviewCampaignsAsync(
            skip,
            boundedPageSize,
            cancellationToken);
        var items = page.Items
            .Select(item => new PendingCampaignSummaryDto(
                item.CampaignId,
                item.CompanyId,
                item.CompanyName,
                item.Title,
                CreateDescriptionSummary(item.Description),
                item.Status.ToString(),
                item.SubmittedAtUtc,
                item.TargetCount,
                item.HasApprovedMedia,
                GetReadinessIssues(
                    item.Title,
                    item.Description,
                    item.TargetCount,
                    item.HasReviewableMedia,
                    item.HasApprovedMedia)))
            .ToArray();

        return new PendingCampaignPageDto(items, boundedPageNumber, boundedPageSize, page.TotalCount);
    }

    public async Task<CampaignReviewDetailDto> GetReviewDetailAsync(
        string adminUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        await GetApprovedAdminIdAsync(adminUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        var detail = await domainUnitOfWork.Campaigns.FindPendingReviewDetailAsync(campaignId, cancellationToken)
            ?? throw new WorkflowNotFoundException("Not found.");
        var reviewableFiles = await domainUnitOfWork.StoredFiles.ListActiveReviewableCampaignFilesAsync(
            detail.CampaignId,
            cancellationToken);
        var optionalFiles = await domainUnitOfWork.StoredFiles.ListActiveOptionalCampaignFilesAsync(
            detail.CampaignId,
            cancellationToken);

        return new CampaignReviewDetailDto(
            detail.CampaignId,
            detail.CompanyId,
            detail.CompanyName,
            detail.Title,
            detail.Description,
            detail.Status.ToString(),
            detail.SubmittedAtUtc,
            detail.TargetCount,
            await MapReviewFilesAsync(adminUserId, reviewableFiles, cancellationToken),
            await MapReviewFilesAsync(adminUserId, optionalFiles, cancellationToken),
            GetReadinessIssues(
                detail.Title,
                detail.Description,
                detail.TargetCount,
                detail.HasReviewableMedia,
                detail.HasApprovedMedia));
    }

    public async Task<CampaignAssetDto> ReviewAssetAsync(
        string adminUserId,
        string assetId,
        ReviewDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid || request.Decision is not ("Approved" or "Rejected"))
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var adminId = await GetApprovedAdminIdForUpdateAsync(adminUserId, transactionCancellationToken);
            if (string.IsNullOrWhiteSpace(assetId))
            {
                throw new WorkflowNotFoundException("Not found.");
            }

            var asset = await domainUnitOfWork.StoredFiles.FindStoredFileForUpdateAsync(assetId, transactionCancellationToken)
                ?? throw new WorkflowNotFoundException("Not found.");
            if (asset.OwnerType != StoredFileOwnerType.Campaign || asset.Purpose != StoredFilePurpose.CampaignMedia)
            {
                throw new WorkflowNotFoundException("Not found.");
            }

            if (asset.ReviewStatus != StoredFileReviewStatus.Pending)
            {
                throw new WorkflowConflictException("Campaign asset has already been reviewed.");
            }

            var now = DateTime.UtcNow;
            asset.ReviewStatus = request.Decision == "Approved"
                ? StoredFileReviewStatus.Approved
                : StoredFileReviewStatus.Rejected;
            asset.ReviewedAtUtc = now;
            asset.ReviewedByAdminId = adminId;
            asset.ReviewReason = CampaignReviewTransitionPolicy.NormalizeReason(request.Reason);

            await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "CampaignAssetReviewed",
                AuditOutcome.Success,
                now,
                $"Campaign asset {asset.ReviewStatus}.",
                targetType: AuditTargetType.Campaign,
                targetId: asset.OwnerId,
                cancellationToken: transactionCancellationToken);

            return ToAssetDto(asset);
        }, cancellationToken);
    }

    public async Task<CampaignReviewResultDto> ReviewCampaignAsync(
        string adminUserId,
        string campaignId,
        string idempotencyKey,
        ReviewDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        var safeKey = ValidateIdempotencyKey(idempotencyKey);
        var decision = CampaignReviewTransitionPolicy.ParseDecision(request.Decision);
        var normalizedReason = CampaignReviewTransitionPolicy.NormalizeReason(request.Reason);
        var normalizedNotes = CampaignReviewTransitionPolicy.NormalizeReason(request.Notes);
        try
        {
            return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                var adminId = await GetApprovedAdminIdForUpdateAsync(adminUserId, transactionCancellationToken);
                var campaignSnapshot = await FindCampaignAsync(campaignId, transactionCancellationToken);
                var replayResult = await TryCreateReplayResultAsync(
                    adminId,
                    campaignSnapshot.Id,
                    safeKey,
                    decision,
                    normalizedReason,
                    normalizedNotes,
                    transactionCancellationToken);
                if (replayResult is not null)
                {
                    return replayResult;
                }

                if (decision == CampaignReviewDecision.Approved)
                {
                    await LockApprovedOwningCompanyAsync(campaignSnapshot.CompanyId, transactionCancellationToken);
                }

                var campaign = await FindCampaignForUpdateAsync(campaignSnapshot.Id, transactionCancellationToken);
                if (!string.Equals(campaign.CompanyId, campaignSnapshot.CompanyId, StringComparison.Ordinal))
                {
                    throw new WorkflowConflictException("Campaign ownership changed during review.");
                }

                replayResult = await TryCreateReplayResultAsync(
                    adminId,
                    campaign.Id,
                    safeKey,
                    decision,
                    normalizedReason,
                    normalizedNotes,
                    transactionCancellationToken);
                if (replayResult is not null)
                {
                    return replayResult;
                }

                CampaignReviewTransitionPolicy.ValidateNewDecision(campaign.Status, decision, normalizedReason);
                var approvalTargets = decision == CampaignReviewDecision.Approved
                    ? await ValidateApprovalPrerequisitesAsync(campaign, transactionCancellationToken)
                    : null;

                var now = DateTime.UtcNow;
                var priorStatus = campaign.Status;
                var resultingStatus = CampaignReviewTransitionPolicy.GetResultStatus(decision);
                var reviewHistory = new CampaignReviewHistory
                {
                    CampaignId = campaign.Id,
                    AdminUserId = adminId,
                    Decision = decision,
                    IdempotencyKey = safeKey,
                    Reason = normalizedReason,
                    Notes = normalizedNotes,
                    PriorStatus = priorStatus,
                    ResultingStatus = resultingStatus,
                    CreatedAtUtc = now
                };
                await domainUnitOfWork.Campaigns.AddCampaignReviewHistoryAsync(
                    reviewHistory,
                    transactionCancellationToken);

                var queuedCount = 0;
                if (decision == CampaignReviewDecision.Approved)
                {
                    queuedCount = await ApproveAndQueueAsync(
                        campaign,
                        approvalTargets!,
                        now,
                        transactionCancellationToken);
                }
                else
                {
                    campaign.Status = resultingStatus;
                    campaign.UpdatedAtUtc = now;
                }

                await AddReviewAuditEventAsync(
                    "CampaignReviewed",
                    AuditOutcome.Success,
                    now,
                    adminId,
                    decision,
                    resultingStatus,
                    reviewHistory.Id,
                    queuedCount,
                    campaign.Id,
                    transactionCancellationToken);

                return new CampaignReviewResultDto(
                    campaign.Id,
                    CampaignReviewTransitionPolicy.GetCanonicalDecisionName(decision),
                    campaign.Status.ToString(),
                    queuedCount,
                    now,
                    campaign.Status == CampaignStatus.RevisionRequired);
            }, cancellationToken);
        }
        catch (WorkflowConflictException)
        {
            await domainUnitOfWork.ExecuteInTransactionAsync(
                transactionCancellationToken => AddReviewAuditEventAsync(
                    "CampaignReviewConflict",
                    AuditOutcome.Failed,
                    DateTime.UtcNow,
                    adminUserId,
                    decision,
                    resultingStatus: null,
                    reviewHistoryId: null,
                    queuedCount: null,
                    campaignId,
                    transactionCancellationToken),
                cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<QueueRowDto>> GetQueueRowsAsync(
        string adminUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        await GetApprovedAdminIdAsync(adminUserId, cancellationToken);
        var campaign = await FindCampaignAsync(campaignId, cancellationToken);
        var rows = await domainUnitOfWork.Campaigns.ListCampaignQueueRowsAsync(campaign.Id, cancellationToken);
        return rows
            .Select(row => new QueueRowDto(
                row.QueueId,
                row.CampaignId,
                row.DoctorId,
                row.Status.ToString(),
                row.CampaignSubmittedAtUtc
                    ?? throw new WorkflowConflictException("Queue row submission timestamp is unavailable."),
                row.QueuedAtUtc))
            .ToArray();
    }

    private async Task<IReadOnlyList<CampaignTarget>> ValidateApprovalPrerequisitesAsync(
        Campaign campaign,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaign.Title) || string.IsNullOrWhiteSpace(campaign.Description))
        {
            throw new WorkflowValidationException("Campaign text is required before approval.");
        }

        var targets = await domainUnitOfWork.Campaigns.ListCampaignTargetsAsync(campaign.Id, cancellationToken);
        if (targets.Count == 0)
        {
            throw new WorkflowValidationException("At least one submitted target is required before approval.");
        }

        if (!await domainUnitOfWork.StoredFiles.HasActiveApprovedCampaignMediaAsync(campaign.Id, cancellationToken))
        {
            throw new WorkflowValidationException("At least one active approved campaign media asset is required before approval.");
        }

        if (campaign.SubmittedAtUtc is null)
        {
            throw new WorkflowValidationException("Campaign submission timestamp is required before approval.");
        }

        return targets;
    }

    private async Task LockApprovedOwningCompanyAsync(
        string companyId,
        CancellationToken cancellationToken)
    {
        var companySnapshot = await identityUnitOfWork.Profiles.FindCompanyProfileByIdAsync(companyId, cancellationToken);
        if (companySnapshot is null)
        {
            throw new WorkflowValidationException("Campaign owner company must be approved and active before approval.");
        }

        var companyUser = await identityUnitOfWork.Users.FindByIdForUpdateAsync(
            companySnapshot.UserId,
            cancellationToken);
        var company = await identityUnitOfWork.Profiles.FindCompanyProfileByIdForUpdateAsync(
            companyId,
            cancellationToken);
        if (company is null
            || !string.Equals(company.UserId, companySnapshot.UserId, StringComparison.Ordinal)
            || companyUser is null
            || companyUser.Role != UserRole.Company
            || companyUser.AccountStatus != AccountStatus.Approved
            || companyUser.IsDeleted)
        {
            throw new WorkflowValidationException("Campaign owner company must be approved and active before approval.");
        }
    }

    private async Task<int> ApproveAndQueueAsync(
        Campaign campaign,
        IReadOnlyList<CampaignTarget> targets,
        DateTime approvedAtUtc,
        CancellationToken cancellationToken)
    {
        var submittedAtUtc = campaign.SubmittedAtUtc
            ?? throw new WorkflowValidationException("Campaign submission timestamp is required before approval.");
        var targetDoctorIds = targets.Select(target => target.DoctorId).Distinct(StringComparer.Ordinal).ToArray();
        var activeDoctorIds = (await domainUnitOfWork.MessageQueues.ListActiveQueueDoctorIdsAsync(
                campaign.Id,
                targetDoctorIds,
                cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!activeDoctorIds.Add(target.DoctorId))
            {
                continue;
            }

            await domainUnitOfWork.MessageQueues.AddQueueItemAsync(new DoctorMessageQueue
            {
                CampaignId = campaign.Id,
                DoctorId = target.DoctorId,
                QueuedAtUtc = approvedAtUtc,
                CampaignSubmittedAtUtc = submittedAtUtc,
                Status = QueueItemStatus.Queued,
                CreatedAtUtc = approvedAtUtc
            }, cancellationToken);
        }

        campaign.Status = CampaignStatus.Approved;
        campaign.UpdatedAtUtc = approvedAtUtc;
        return activeDoctorIds.Count;
    }

    private Task<string> GetApprovedAdminIdAsync(string adminUserId, CancellationToken cancellationToken)
        => ResolveApprovedAdminIdAsync(adminUserId, useUpdateLock: false, cancellationToken);

    private Task<string> GetApprovedAdminIdForUpdateAsync(string adminUserId, CancellationToken cancellationToken)
        => ResolveApprovedAdminIdAsync(adminUserId, useUpdateLock: true, cancellationToken);

    private async Task<string> ResolveApprovedAdminIdAsync(
        string adminUserId,
        bool useUpdateLock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new WorkflowUnauthorizedException("Authentication denied.");
        }

        var admin = useUpdateLock
            ? await identityUnitOfWork.Users.FindByIdForUpdateAsync(adminUserId, cancellationToken)
            : await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (admin is null)
        {
            throw new WorkflowUnauthorizedException("Authentication denied.");
        }

        if (admin.Role != UserRole.Admin || admin.AccountStatus != AccountStatus.Approved || admin.IsDeleted)
        {
            throw new WorkflowForbiddenException("Forbidden.");
        }

        return admin.Id;
    }

    private async Task<Campaign> FindCampaignAsync(string campaignId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        return await domainUnitOfWork.Campaigns.FindActiveCampaignAsync(campaignId, cancellationToken)
            ?? throw new WorkflowNotFoundException("Not found.");
    }

    private async Task<Campaign> FindCampaignForUpdateAsync(string campaignId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        return await domainUnitOfWork.Campaigns.FindActiveCampaignForUpdateAsync(campaignId, cancellationToken)
            ?? throw new WorkflowNotFoundException("Not found.");
    }

    private async Task<CampaignReviewResultDto> CreateReplayResultAsync(
        CampaignReviewHistory priorReview,
        CancellationToken cancellationToken)
    {
        var queuedCount = priorReview.ResultingStatus == CampaignStatus.Approved
            ? await domainUnitOfWork.Campaigns.CountCampaignQueueItemsAsync(priorReview.CampaignId, cancellationToken)
            : 0;

        return new CampaignReviewResultDto(
            priorReview.CampaignId,
            CampaignReviewTransitionPolicy.GetCanonicalDecisionName(priorReview.Decision),
            priorReview.ResultingStatus.ToString(),
            queuedCount,
            priorReview.CreatedAtUtc,
            priorReview.ResultingStatus == CampaignStatus.RevisionRequired);
    }

    private async Task<CampaignReviewResultDto?> TryCreateReplayResultAsync(
        string reviewerId,
        string campaignId,
        string idempotencyKey,
        CampaignReviewDecision requestedDecision,
        string? requestedReason,
        string? requestedNotes,
        CancellationToken cancellationToken)
    {
        var priorReview = await domainUnitOfWork.Campaigns.FindCampaignReviewByIdempotencyKeyAsync(
            campaignId,
            idempotencyKey,
            cancellationToken);
        if (priorReview is null)
        {
            return null;
        }

        if (!CampaignReviewTransitionPolicy.IsReplayEquivalent(
                priorReview.Decision,
                priorReview.Reason,
                priorReview.Notes,
                requestedDecision,
                requestedReason,
                requestedNotes))
        {
            throw new WorkflowConflictException("Idempotency conflict.");
        }

        var result = await CreateReplayResultAsync(priorReview, cancellationToken);
        await AddReviewAuditEventAsync(
            "CampaignReviewReplayed",
            AuditOutcome.Info,
            DateTime.UtcNow,
            reviewerId,
            priorReview.Decision,
            priorReview.ResultingStatus,
            priorReview.Id,
            result.QueuedCount,
            campaignId,
            cancellationToken);
        return result;
    }

    private Task AddReviewAuditEventAsync(
        string eventType,
        AuditOutcome outcome,
        DateTime createdAtUtc,
        string reviewerId,
        CampaignReviewDecision decision,
        CampaignStatus? resultingStatus,
        string? reviewHistoryId,
        int? queuedCount,
        string campaignId,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            ReviewerId = reviewerId,
            Decision = CampaignReviewTransitionPolicy.GetCanonicalDecisionName(decision),
            ResultingStatus = resultingStatus?.ToString(),
            ReviewHistoryId = reviewHistoryId,
            QueuedCount = queuedCount
        });
        return domainUnitOfWork.AuditEvents.AddAuditEventAsync(
            Guid.NewGuid().ToString("N"),
            eventType,
            outcome,
            createdAtUtc,
            metadata,
            targetType: AuditTargetType.Campaign,
            targetId: campaignId,
            cancellationToken: cancellationToken);
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

    private static CampaignAssetDto ToAssetDto(MediBridge.Core.Entities.Files.StoredFile asset)
        => new(asset.Id, asset.OwnerId, asset.ReviewStatus.ToString(), asset.ReviewReason);

    private async Task<IReadOnlyList<ReviewFileDto>> MapReviewFilesAsync(
        string adminUserId,
        IReadOnlyList<MediBridge.Core.Entities.Files.StoredFile> files,
        CancellationToken cancellationToken)
    {
        var result = new List<ReviewFileDto>(files.Count);
        foreach (var file in files)
        {
            var access = await fileAccessService.GetSignedAccessAsync(adminUserId, file.Id, cancellationToken);
            result.Add(new ReviewFileDto(
                file.Id,
                file.Purpose.ToString(),
                file.OriginalFileName,
                file.ContentType,
                file.SizeBytes,
                file.ReviewStatus.ToString(),
                access.Url,
                access.ExpiresAtUtc));
        }

        return result;
    }

    private static string CreateDescriptionSummary(string description)
    {
        const int maximumLength = 200;
        var normalized = description?.Trim() ?? string.Empty;
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static IReadOnlyList<string> GetReadinessIssues(
        string title,
        string description,
        int targetCount,
        bool hasReviewableMedia,
        bool hasApprovedMedia)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(title))
        {
            issues.Add("Campaign title is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            issues.Add("Campaign description is required.");
        }

        if (targetCount <= 0)
        {
            issues.Add("At least one submitted target is required.");
        }

        if (!hasReviewableMedia)
        {
            issues.Add("At least one active reviewable campaign media asset is required.");
        }
        else if (!hasApprovedMedia)
        {
            issues.Add("At least one approved campaign media asset is required.");
        }

        return issues;
    }
}
