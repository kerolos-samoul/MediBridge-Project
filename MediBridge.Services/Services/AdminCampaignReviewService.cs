using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.DTOs.Files;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class AdminCampaignReviewService : IAdminCampaignReviewService
{
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IFileWorkflowService fileWorkflowService;
    private readonly IValidator<ReviewDecisionRequestDto> reviewDecisionValidator;

    public AdminCampaignReviewService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IFileWorkflowService fileWorkflowService,
        IValidator<ReviewDecisionRequestDto> reviewDecisionValidator)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.fileWorkflowService = fileWorkflowService;
        this.reviewDecisionValidator = reviewDecisionValidator;
    }

    public async Task<PendingCampaignPageDto> ListPendingCampaignsAsync(
        string adminUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await EnsureApprovedAdminAsync(adminUserId, cancellationToken);
        var boundedPageNumber = Math.Max(1, pageNumber);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);
        var skipValue = ((long)boundedPageNumber - 1L) * boundedPageSize;
        var skip = skipValue > int.MaxValue ? int.MaxValue : (int)skipValue;
        var page = await domainUnitOfWork.Campaigns.ListPendingReviewCampaignsAsync(
            skip,
            boundedPageSize,
            cancellationToken);
        var items = page.Items.Select(item => new PendingCampaignSummaryDto(
            item.CampaignId,
            item.CompanyId,
            item.CompanyName,
            item.Title,
            CreateDescriptionSummary(item.Description),
            item.Status.ToString(),
            item.SubmittedAtUtc,
            item.TargetCount,
            item.HasApprovedMedia,
            GetReadinessIssues(item.Title, item.Description, item.TargetCount, item.HasReviewableMedia, item.HasApprovedMedia)))
            .ToArray();

        return new PendingCampaignPageDto(items, boundedPageNumber, boundedPageSize, page.TotalCount);
    }

    public async Task<CampaignReviewDetailDto> GetReviewDetailAsync(
        string adminUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        await EnsureApprovedAdminAsync(adminUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new Phase5NotFoundException("Not found.");
        }

        var detail = await domainUnitOfWork.Campaigns.FindPendingReviewDetailAsync(campaignId, cancellationToken)
            ?? throw new Phase5NotFoundException("Not found.");
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
            GetReadinessIssues(detail.Title, detail.Description, detail.TargetCount, detail.HasReviewableMedia, detail.HasApprovedMedia));
    }

    public async Task<CampaignReviewResultDto> ReviewCampaignAsync(
        string adminUserId,
        string campaignId,
        string idempotencyKey,
        ReviewDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await reviewDecisionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                validation.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        var key = NormalizeIdempotencyKey(idempotencyKey);
        var decision = CampaignReviewTransitionPolicy.ParseDecision(request.Decision);
        var reason = CampaignReviewTransitionPolicy.Normalize(request.Reason);
        var notes = CampaignReviewTransitionPolicy.Normalize(request.Notes);

        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var reviewerId = await EnsureApprovedAdminForUpdateAsync(adminUserId, transactionCancellationToken);
            if (string.IsNullOrWhiteSpace(campaignId))
            {
                throw new Phase5NotFoundException("Not found.");
            }

            var campaign = await domainUnitOfWork.Campaigns.FindActiveCampaignForUpdateAsync(
                    campaignId,
                    transactionCancellationToken)
                ?? throw new Phase5NotFoundException("Not found.");
            var priorReview = await domainUnitOfWork.Campaigns.FindCampaignReviewByIdempotencyKeyAsync(
                campaign.Id,
                key,
                transactionCancellationToken);
            if (priorReview is not null)
            {
                if (!CampaignReviewTransitionPolicy.IsReplayEquivalent(
                        priorReview.Decision,
                        priorReview.Reason,
                        priorReview.Notes,
                        decision,
                        reason,
                        notes))
                {
                    throw new Phase5ConflictException("Idempotency conflict.");
                }

                var replayCount = priorReview.ResultingStatus == CampaignStatus.Approved
                    ? await domainUnitOfWork.Campaigns.CountCampaignQueueItemsAsync(campaign.Id, transactionCancellationToken)
                    : 0;
                await AddReviewAuditAsync(
                    "CampaignReviewReplayed",
                    reviewerId,
                    campaign.Id,
                    decision,
                    priorReview.PriorStatus,
                    priorReview.ResultingStatus,
                    priorReview.Id,
                    priorReview.Reason,
                    replayCount,
                    AuditOutcome.Info,
                    transactionCancellationToken);
                return ToReviewResult(priorReview, replayCount);
            }

            CampaignReviewTransitionPolicy.ValidateNewDecision(campaign.Status, decision, reason);
            IReadOnlyList<CampaignTarget> approvalTargets = Array.Empty<CampaignTarget>();
            if (decision == CampaignReviewDecision.Approved)
            {
                await EnsureApprovedCompanyForUpdateAsync(campaign.CompanyId, transactionCancellationToken);
                approvalTargets = await ValidateApprovalPrerequisitesAsync(campaign, transactionCancellationToken);
            }

            var now = DateTime.UtcNow;
            var resultingStatus = CampaignReviewTransitionPolicy.GetResultStatus(decision);
            var history = new CampaignReviewHistory
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = campaign.Id,
                AdminUserId = reviewerId,
                Decision = decision,
                IdempotencyKey = key,
                Reason = reason,
                Notes = notes,
                PriorStatus = campaign.Status,
                ResultingStatus = resultingStatus,
                CreatedAtUtc = now
            };
            await domainUnitOfWork.Campaigns.AddCampaignReviewHistoryAsync(history, transactionCancellationToken);

            var queuedCount = 0;
            if (decision == CampaignReviewDecision.Approved)
            {
                queuedCount = await CreateMissingQueueRowsAsync(campaign, approvalTargets, now, transactionCancellationToken);
            }

            campaign.Status = resultingStatus;
            campaign.UpdatedAtUtc = now;
            await AddReviewAuditAsync(
                "CampaignReviewCompleted",
                reviewerId,
                campaign.Id,
                decision,
                history.PriorStatus,
                resultingStatus,
                history.Id,
                reason,
                queuedCount,
                AuditOutcome.Success,
                transactionCancellationToken);
            return ToReviewResult(history, queuedCount);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<QueueRowDto>> GetQueueRowsAsync(
        string adminUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        await EnsureApprovedAdminAsync(adminUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(campaignId)
            || await domainUnitOfWork.Campaigns.FindActiveCampaignAsync(campaignId, cancellationToken) is null)
        {
            throw new Phase5NotFoundException("Not found.");
        }

        var rows = await domainUnitOfWork.Campaigns.ListCampaignQueueRowsAsync(campaignId, cancellationToken);
        return rows.Select(row => new QueueRowDto(
            row.QueueId,
            row.CampaignId,
            row.DoctorId,
            row.Status.ToString(),
            row.CampaignSubmittedAtUtc
                ?? throw new Phase5ConflictException("Queue row submission timestamp is unavailable."),
            row.QueuedAtUtc)).ToArray();
    }

    private async Task<IReadOnlyList<CampaignTarget>> ValidateApprovalPrerequisitesAsync(
        Campaign campaign,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaign.Title) || string.IsNullOrWhiteSpace(campaign.Description))
        {
            throw new Phase5ValidationException("Campaign text is required before approval.");
        }

        var targets = await domainUnitOfWork.Campaigns.ListCampaignTargetsAsync(campaign.Id, cancellationToken);
        if (targets.Count == 0)
        {
            throw new Phase5ValidationException("At least one submitted target is required before approval.");
        }

        if (!await domainUnitOfWork.StoredFiles.HasActiveApprovedCampaignMediaAsync(campaign.Id, cancellationToken))
        {
            throw new Phase5ValidationException("At least one active approved campaign media asset is required before approval.");
        }

        if (campaign.SubmittedAtUtc is null)
        {
            throw new Phase5ValidationException("Campaign submission timestamp is required before approval.");
        }

        return targets;
    }

    private async Task<int> CreateMissingQueueRowsAsync(
        Campaign campaign,
        IReadOnlyList<CampaignTarget> targets,
        DateTime queuedAtUtc,
        CancellationToken cancellationToken)
    {
        var submittedAtUtc = campaign.SubmittedAtUtc
            ?? throw new Phase5ValidationException("Campaign submission timestamp is required before approval.");
        var created = new List<DoctorMessageQueue>();
        foreach (var doctorId in targets.Select(target => target.DoctorId).Distinct(StringComparer.Ordinal))
        {
            if (await domainUnitOfWork.MessageQueues.QueueItemExistsAsync(campaign.Id, doctorId, cancellationToken))
            {
                continue;
            }

            created.Add(new DoctorMessageQueue
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = campaign.Id,
                DoctorId = doctorId,
                CampaignSubmittedAtUtc = submittedAtUtc,
                QueuedAtUtc = queuedAtUtc,
                CreatedAtUtc = queuedAtUtc,
                Status = QueueItemStatus.Queued
            });
        }

        if (created.Count > 0)
        {
            await domainUnitOfWork.MessageQueues.AddQueueItemsAsync(created, cancellationToken);
        }

        return await domainUnitOfWork.Campaigns.CountCampaignQueueItemsAsync(campaign.Id, cancellationToken)
            + created.Count;
    }

    private async Task EnsureApprovedCompanyForUpdateAsync(string companyId, CancellationToken cancellationToken)
    {
        var snapshot = await identityUnitOfWork.Profiles.FindCompanyProfileByIdAsync(companyId, cancellationToken)
            ?? throw new Phase5ValidationException("Campaign owner company must be approved and active before approval.");
        var user = await identityUnitOfWork.Users.FindByIdForUpdateAsync(snapshot.UserId, cancellationToken);
        var company = await identityUnitOfWork.Profiles.FindCompanyProfileByIdForUpdateAsync(companyId, cancellationToken);
        if (company is null
            || user is not { Role: UserRole.Company, AccountStatus: AccountStatus.Approved, IsDeleted: false }
            || !string.Equals(company.UserId, snapshot.UserId, StringComparison.Ordinal))
        {
            throw new Phase5ValidationException("Campaign owner company must be approved and active before approval.");
        }
    }

    private async Task<string> EnsureApprovedAdminForUpdateAsync(string adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var admin = await identityUnitOfWork.Users.FindByIdForUpdateAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        return admin.Id;
    }

    private Task AddReviewAuditAsync(
        string eventType,
        string reviewerId,
        string campaignId,
        CampaignReviewDecision decision,
        CampaignStatus priorStatus,
        CampaignStatus resultingStatus,
        string reviewHistoryId,
        string? reason,
        int queuedCount,
        AuditOutcome outcome,
        CancellationToken cancellationToken)
    {
        var createdAtUtc = DateTime.UtcNow;
        var metadata = JsonSerializer.Serialize(new
        {
            ReviewerId = reviewerId,
            Decision = CampaignReviewTransitionPolicy.GetCanonicalDecisionName(decision),
            PriorStatus = priorStatus.ToString(),
            ResultingStatus = resultingStatus.ToString(),
            ReviewHistoryId = reviewHistoryId,
            QueuedCount = queuedCount,
            ReviewedAtUtc = createdAtUtc
        });
        return domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            Guid.NewGuid().ToString("N"),
            eventType,
            reviewerId,
            UserRole.Admin.ToString(),
            AuditTargetType.Campaign,
            campaignId,
            outcome,
            reason ?? "Campaign moderation action.",
            correlationId: null,
            metadata,
            createdAtUtc,
            cancellationToken);
    }

    private static CampaignReviewResultDto ToReviewResult(CampaignReviewHistory history, int queuedCount)
        => new(
            history.CampaignId,
            CampaignReviewTransitionPolicy.GetCanonicalDecisionName(history.Decision),
            history.ResultingStatus.ToString(),
            queuedCount,
            history.CreatedAtUtc,
            history.ResultingStatus == CampaignStatus.RevisionRequired);

    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        var value = idempotencyKey?.Trim() ?? string.Empty;
        if (value.Length is < 8 or > 128)
        {
            throw new Phase5ValidationException("Validation failed.", ["Idempotency-Key length must be between 8 and 128 characters."]);
        }

        return value;
    }

    private async Task EnsureApprovedAdminAsync(string adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var admin = await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }
    }

    private async Task<IReadOnlyList<ReviewFileDto>> MapReviewFilesAsync(
        string adminUserId,
        IReadOnlyList<StoredFile> files,
        CancellationToken cancellationToken)
    {
        var result = new List<ReviewFileDto>(files.Count);
        foreach (var file in files)
        {
            FileAccessGrantDto access;
            try
            {
                access = await fileWorkflowService.CreatePrivateAccessGrantAsync(
                    adminUserId,
                    UserRole.Admin,
                    file.Id,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidOperationException exception)
            {
                throw new Phase5ServiceUnavailableException(
                    "Review file storage is unavailable.",
                    exception);
            }

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
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static IReadOnlyList<string> GetReadinessIssues(
        string title,
        string description,
        int targetCount,
        bool hasReviewableMedia,
        bool hasApprovedMedia)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(title)) issues.Add("Campaign title is required.");
        if (string.IsNullOrWhiteSpace(description)) issues.Add("Campaign description is required.");
        if (targetCount <= 0) issues.Add("At least one submitted target is required.");
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
