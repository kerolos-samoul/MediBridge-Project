using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class CampaignWorkflowService : ICampaignWorkflowService
{
    private static readonly JsonSerializerOptions HashJsonOptions = new() { PropertyNamingPolicy = null };
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IValidator<CreateCampaignRequestDto> createCampaignValidator;
    private readonly IValidator<UpdateCampaignRequestDto> updateCampaignValidator;

    public CampaignWorkflowService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IValidator<CreateCampaignRequestDto> createCampaignValidator,
        IValidator<UpdateCampaignRequestDto> updateCampaignValidator)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.createCampaignValidator = createCampaignValidator;
        this.updateCampaignValidator = updateCampaignValidator;
    }

    public Task<CampaignDraftDto> UpdateCampaignAsync(
        string actorUserId,
        string campaignId,
        UpdateCampaignRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Campaign request body is required."]);
        }

        return UpdateCampaignCoreAsync(actorUserId, campaignId, request, cancellationToken);
    }

    public Task<CampaignSubmissionDto> SubmitCampaignAsync(
        string actorUserId,
        string campaignId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeIdempotencyKey(idempotencyKey);
        return domainUnitOfWork.ExecuteInTransactionAsync(
            transactionCancellationToken => SubmitExistingCampaignAsync(
                actorUserId,
                campaignId,
                key,
                transactionCancellationToken),
            cancellationToken);
    }

    public Task<CompanyReviewOutcomeDto> GetReviewOutcomeAsync(
        string actorUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
        => GetReviewOutcomeCoreAsync(actorUserId, campaignId, cancellationToken);

    private async Task<CampaignDraftDto> UpdateCampaignCoreAsync(
        string actorUserId,
        string campaignId,
        UpdateCampaignRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await updateCampaignValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                validation.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var company = await ResolveApprovedCompanyActorForUpdateAsync(actorUserId, transactionCancellationToken);
            var campaign = await FindOwnedCampaignForUpdateAsync(company.CompanyId, campaignId, transactionCancellationToken);
            EnsureCompanyEditableCampaign(campaign);

            campaign.Title = request.Title.Trim();
            campaign.Description = request.Description.Trim();
            campaign.ClinicalResearchInfo = NormalizeOptionalText(request.ClinicalResearchInfo);
            campaign.UpdatedAtUtc = DateTime.UtcNow;
            return ToCampaignDraftDto(campaign);
        }, cancellationToken);
    }

    private async Task<CampaignSubmissionDto> SubmitExistingCampaignAsync(
        string actorUserId,
        string campaignId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var company = await ResolveApprovedCompanyActorForUpdateAsync(actorUserId, cancellationToken);
        var campaign = await FindOwnedCampaignForUpdateAsync(company.CompanyId, campaignId, cancellationToken);
        var keyedAttempt = await domainUnitOfWork.Campaigns.FindCampaignSubmissionAttemptByKeyAsync(
            campaign.Id,
            idempotencyKey,
            cancellationToken);
        var currentAttempt = await domainUnitOfWork.Campaigns.FindCurrentCampaignSubmissionAttemptAsync(
            campaign.Id,
            cancellationToken);

        if (campaign.Status == CampaignStatus.PendingReview
            && keyedAttempt is not null
            && currentAttempt is not null
            && string.Equals(keyedAttempt.Id, currentAttempt.Id, StringComparison.Ordinal))
        {
            return ToCampaignSubmissionDto(keyedAttempt);
        }

        if (campaign.Status == CampaignStatus.PendingReview || keyedAttempt is not null)
        {
            throw new Phase5ConflictException("Idempotency conflict.");
        }

        EnsureCompanyEditableCampaign(campaign);
        if (string.IsNullOrWhiteSpace(campaign.Title) || string.IsNullOrWhiteSpace(campaign.Description))
        {
            throw new Phase5ValidationException("Campaign text is required before submission.");
        }

        var reviewableMedia = await domainUnitOfWork.StoredFiles.ListActiveReviewableCampaignFilesAsync(
            campaign.Id,
            cancellationToken);
        if (reviewableMedia.Count == 0)
        {
            throw new Phase5ValidationException("At least one active pending or approved campaign media asset is required before submission.");
        }

        var criteria = new EligibleDoctorSearchCriteria(null, null, null, null, null, null, null);
        var eligibleCount = await identityUnitOfWork.Profiles.CountEligibleDoctorsAsync(criteria, cancellationToken);
        if (eligibleCount == 0)
        {
            throw new Phase5ValidationException("At least one eligible priced doctor is required before submission.");
        }

        var doctors = await identityUnitOfWork.Profiles.SearchEligibleDoctorsAsync(
            criteria,
            0,
            eligibleCount,
            cancellationToken);
        var submittedAtUtc = DateTime.UtcNow;
        var targets = doctors.Select(doctor => new CampaignTarget
        {
            Id = Guid.NewGuid().ToString("N"),
            CampaignId = campaign.Id,
            DoctorId = doctor.Id,
            SpecializationSnapshot = doctor.Specialization,
            ExperienceYearsSnapshot = doctor.ExperienceYears,
            LocationSnapshot = doctor.Location,
            ActivityScoreSnapshot = doctor.ActivityScore,
            PricePerMessageSnapshot = doctor.PricePerMessage ?? 0m,
            CreatedAtUtc = submittedAtUtc
        }).ToArray();
        var estimatedCost = targets.Sum(target => target.PricePerMessageSnapshot);
        var wallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(
            WalletOwnerType.Company,
            company.CompanyId,
            cancellationToken);
        if (wallet is null)
        {
            throw new Phase5ValidationException("The company wallet must be funded before submission.");
        }

        if (wallet.AvailableBalance < estimatedCost)
        {
            throw new Phase5ConflictException("Company wallet available balance is insufficient.");
        }

        await domainUnitOfWork.Campaigns.ReplaceCampaignTargetsAsync(campaign.Id, targets, cancellationToken);
        campaign.Status = CampaignStatus.PendingReview;
        campaign.SubmittedAtUtc = submittedAtUtc;
        campaign.UpdatedAtUtc = submittedAtUtc;
        var attempt = new CampaignSubmissionAttempt
        {
            Id = Guid.NewGuid().ToString("N"),
            CampaignId = campaign.Id,
            IdempotencyKey = idempotencyKey,
            SubmittedAtUtc = submittedAtUtc,
            TargetCount = targets.Length,
            EstimatedCost = estimatedCost,
            Currency = "EGP",
            CreatedAtUtc = submittedAtUtc
        };
        await domainUnitOfWork.Campaigns.AddCampaignSubmissionAttemptAsync(attempt, cancellationToken);
        await AddAuditAsync(
            "CampaignSubmitted",
            actorUserId,
            company.ActorRole,
            AuditTargetType.Campaign,
            campaign.Id,
            AuditOutcome.Success,
            "Campaign submitted after affordability validation.",
            new { campaign.Id, attempt.TargetCount, attempt.EstimatedCost, attempt.Currency },
            cancellationToken);
        return ToCampaignSubmissionDto(attempt);
    }

    private async Task<CompanyReviewOutcomeDto> GetReviewOutcomeCoreAsync(
        string actorUserId,
        string campaignId,
        CancellationToken cancellationToken)
    {
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var campaign = await domainUnitOfWork.Campaigns.FindCompanyCampaignAsync(
                company.CompanyId,
                campaignId,
                cancellationToken)
            ?? throw new Phase5NotFoundException("Campaign was not found.");
        var latestReview = await domainUnitOfWork.Campaigns.FindLatestCampaignReviewHistoryAsync(
            campaign.Id,
            cancellationToken);
        var queuedCount = await domainUnitOfWork.Campaigns.CountCampaignQueueItemsAsync(
            campaign.Id,
            cancellationToken);
        var canEdit = CampaignReviewTransitionPolicy.IsCompanyEditableStatus(campaign.Status);
        return new CompanyReviewOutcomeDto(
            campaign.Id,
            campaign.Status.ToString(),
            latestReview?.Reason,
            latestReview?.CreatedAtUtc,
            canEdit,
            campaign.Status == CampaignStatus.RevisionRequired,
            queuedCount);
    }

    public async Task<CampaignSubmissionResultDto> SubmitCampaignAsync(string actorUserId, string? idempotencyKey, CreateCampaignRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Campaign request body is required."]);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new Phase5ValidationException("Validation failed.", ["Idempotency-Key is required."]);
        }

        var normalizedIdempotencyKey = idempotencyKey.Trim();
        if (normalizedIdempotencyKey.Length is < 8 or > 128)
        {
            throw new Phase5ValidationException("Validation failed.", ["Idempotency-Key length must be between 8 and 128 characters."]);
        }

        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var requestHash = CreateRequestHash(request);
        var processingResult = await domainUnitOfWork.ExecuteInTransactionAsync(
            transactionCancellationToken => ProcessSubmissionUnderIdempotencyLockAsync(
                actorUserId,
                company,
                normalizedIdempotencyKey,
                request,
                requestHash,
                transactionCancellationToken),
            cancellationToken);

        if (processingResult.Exception is not null)
        {
            throw processingResult.Exception;
        }

        return processingResult.Result ?? throw new InvalidOperationException("Campaign submission processing did not produce a result.");
    }

    private async Task<SubmissionProcessingResult> ProcessSubmissionUnderIdempotencyLockAsync(
        string actorUserId,
        CompanyActor company,
        string idempotencyKey,
        CreateCampaignRequestDto request,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existingSubmission = await domainUnitOfWork.Campaigns.FindSubmissionRequestForUpdateAsync(company.CompanyId, idempotencyKey, cancellationToken);
        if (existingSubmission is not null)
        {
            if (!string.Equals(existingSubmission.RequestHash, requestHash, StringComparison.Ordinal))
            {
                await AddAuditAsync("Phase5CampaignSubmissionIdempotencyConflict", actorUserId, company.ActorRole, AuditTargetType.Company, company.CompanyId, AuditOutcome.Denied, "Campaign submission idempotency conflict.", new { company.CompanyId }, cancellationToken);
                return SubmissionProcessingResult.Failure(new Phase5ConflictException("Campaign submission conflicts with a previous request."));
            }

            if (existingSubmission.CampaignId is null)
            {
                return SubmissionProcessingResult.Failure(new Phase5ConflictException("Campaign submission is not replayable."));
            }

            var replayedCampaign = await LoadCompanyCampaignDetailAsync(company.CompanyId, existingSubmission.CampaignId, cancellationToken);
            await AddAuditAsync("Phase5CampaignSubmissionIdempotentReplay", actorUserId, company.ActorRole, AuditTargetType.Campaign, existingSubmission.CampaignId, AuditOutcome.Info, "Campaign submission replayed.", new { existingSubmission.CampaignId }, cancellationToken);
            return SubmissionProcessingResult.Success(new CampaignSubmissionResultDto { Campaign = replayedCampaign, IsIdempotentReplay = true });
        }

        IReadOnlyList<StoredFile> assets;
        IReadOnlyList<CampaignTargetSnapshotDto> targets;
        try
        {
            await ValidateCreateCampaignRequestAsync(actorUserId, company, request, cancellationToken);
            assets = await ValidateAssetsAsync(actorUserId, company, request.AssetIds, cancellationToken);
            targets = await ValidateTargetsAsync(actorUserId, company, request.TargetDoctorIds, cancellationToken);
            await ValidateWalletSufficiencyAsync(actorUserId, company, targets, cancellationToken);
        }
        catch (Phase5WorkflowException ex)
        {
            return SubmissionProcessingResult.Failure(ex);
        }

        var campaignId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var campaign = new Campaign
        {
            Id = campaignId,
            CompanyId = company.CompanyId,
            Title = NormalizeRequiredText(request.Title),
            Description = NormalizeRequiredText(request.Description),
            ClinicalResearchInfo = NormalizeOptionalText(request.ClinicalResearchInfo),
            MediaFileId = assets.FirstOrDefault(asset => asset.Purpose == StoredFilePurpose.CampaignMedia)?.Id,
            VoiceNoteFileId = assets.FirstOrDefault(asset => asset.Purpose == StoredFilePurpose.VoiceNote)?.Id,
            Status = CampaignStatus.PendingReview,
            CreatedAtUtc = now,
            SubmittedAtUtc = now,
            UpdatedAtUtc = now
        };
        var targetEntities = targets.Select(target => new CampaignTarget
        {
            Id = Guid.NewGuid().ToString("N"),
            CampaignId = campaignId,
            DoctorId = target.DoctorId,
            SpecializationSnapshot = target.Specialization,
            ExperienceYearsSnapshot = target.ExperienceYears,
            LocationSnapshot = target.Location,
            ActivityScoreSnapshot = target.ActivityScore,
            PricePerMessageSnapshot = target.PricePerMessage,
            CreatedAtUtc = now
        }).ToArray();
        var submission = new CampaignSubmissionRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = company.CompanyId,
            IdempotencyKey = idempotencyKey,
            CampaignId = campaignId,
            RequestHash = requestHash,
            Status = CampaignSubmissionRequestStatus.Succeeded,
            CreatedAtUtc = now,
            CompletedAtUtc = now
        };
        var attempt = new CampaignSubmissionAttempt
        {
            Id = Guid.NewGuid().ToString("N"),
            CampaignId = campaignId,
            IdempotencyKey = idempotencyKey,
            SubmittedAtUtc = now,
            TargetCount = targetEntities.Length,
            EstimatedCost = CampaignDtoMapper.CalculateTargetPriceTotal(targets),
            Currency = "EGP",
            CreatedAtUtc = now
        };

        foreach (var asset in assets)
        {
            asset.RelatedCampaignId = campaignId;
        }

        await domainUnitOfWork.Campaigns.AddCampaignAsync(campaign, cancellationToken);
        await domainUnitOfWork.Campaigns.AddCampaignTargetsAsync(targetEntities, cancellationToken);
        await domainUnitOfWork.Campaigns.AddSubmissionRequestAsync(submission, cancellationToken);
        await domainUnitOfWork.Campaigns.AddCampaignSubmissionAttemptAsync(attempt, cancellationToken);
        await AddAuditAsync("Phase5CampaignSubmissionSucceeded", actorUserId, company.ActorRole, AuditTargetType.Campaign, campaignId, AuditOutcome.Success, "Campaign submitted for review.", new { CampaignId = campaignId, TargetCount = targetEntities.Length, AssetCount = assets.Count }, cancellationToken);

        var detail = CampaignDtoMapper.ToDetail(campaign, assets, targetEntities);
        return SubmissionProcessingResult.Success(new CampaignSubmissionResultDto { Campaign = detail, IsIdempotentReplay = false });
    }

    public async Task<CampaignPageDto> GetCompanyCampaignsAsync(string actorUserId, CampaignStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePagination(pageNumber, pageSize);
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var skip = (pageNumber - 1) * pageSize;
        var totalCount = await domainUnitOfWork.Campaigns.CountCompanyCampaignsAsync(company.CompanyId, status, cancellationToken);
        var campaigns = await domainUnitOfWork.Campaigns.ListCompanyCampaignsAsync(company.CompanyId, status, skip, pageSize, cancellationToken);
        var summaries = new List<CampaignSummaryDto>(campaigns.Count);
        foreach (var campaign in campaigns)
        {
            var targetCount = await domainUnitOfWork.Campaigns.CountCampaignTargetsAsync(campaign.Id, cancellationToken);
            summaries.Add(CampaignDtoMapper.ToSummary(campaign, targetCount));
        }

        return new CampaignPageDto
        {
            Page = new CampaignPageMetadataDto
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount
            },
            Items = summaries
        };
    }

    public async Task<CampaignDetailDto> GetCompanyCampaignDetailAsync(string actorUserId, string campaignId, CancellationToken cancellationToken = default)
    {
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        return await LoadCompanyCampaignDetailAsync(company.CompanyId, campaignId, cancellationToken);
    }

    public async Task<TargetPreviewDto> PreviewTargetsAsync(
        string actorUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        _ = await domainUnitOfWork.Campaigns.FindCompanyCampaignAsync(
                company.CompanyId,
                campaignId,
                cancellationToken)
            ?? throw new Phase5NotFoundException("Campaign was not found.");

        var criteria = new EligibleDoctorSearchCriteria(null, null, null, null, null, null, null);
        var eligibleDoctorCount = await identityUnitOfWork.Profiles.CountEligibleDoctorsAsync(
            criteria,
            cancellationToken);
        if (eligibleDoctorCount == 0)
        {
            return new TargetPreviewDto(0, 0m, "EGP");
        }

        var doctors = await identityUnitOfWork.Profiles.SearchEligibleDoctorsAsync(
            criteria,
            0,
            eligibleDoctorCount,
            cancellationToken);
        return new TargetPreviewDto(
            doctors.Count,
            doctors.Sum(doctor => doctor.PricePerMessage ?? 0m),
            "EGP");
    }

    public async Task<QueueSummaryDto> GetQueueSummaryAsync(
        string actorUserId,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        _ = await domainUnitOfWork.Campaigns.FindCompanyCampaignAsync(
                company.CompanyId,
                campaignId,
                cancellationToken)
            ?? throw new Phase5NotFoundException("Campaign was not found.");

        var counts = await domainUnitOfWork.MessageQueues.CountQueueItemsByCampaignAsync(
            campaignId,
            cancellationToken);
        return new QueueSummaryDto(
            campaignId,
            GetQueueStatusCount(counts, QueueItemStatus.Queued),
            GetQueueStatusCount(counts, QueueItemStatus.Activated),
            GetQueueStatusCount(counts, QueueItemStatus.Cancelled),
            0,
            DateTime.UtcNow);
    }

    public Task<CampaignQueueCreationResultDto> CreateQueueForApprovedCampaignAsync(string campaignId, DateTime queuedAtUtc, string? actorUserId = null, CancellationToken cancellationToken = default)
    {
        return domainUnitOfWork.ExecuteInTransactionAsync(
            transactionCancellationToken => CreateQueueForApprovedCampaignInTransactionAsync(campaignId, queuedAtUtc, actorUserId, transactionCancellationToken),
            cancellationToken);
    }

    private async Task<CampaignQueueCreationResultDto> CreateQueueForApprovedCampaignInTransactionAsync(string campaignId, DateTime queuedAtUtc, string? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new Phase5ValidationException("Validation failed.", ["Campaign id is required."]);
        }

        var normalizedCampaignId = campaignId.Trim();
        var auditActorId = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
        const string auditActorRole = "System";
        var campaign = await domainUnitOfWork.Campaigns.FindApprovedCampaignForQueueAsync(normalizedCampaignId, cancellationToken);
        if (campaign is null)
        {
            await AddAuditAsync("Phase5QueueCreationNoOp", auditActorId, auditActorRole, AuditTargetType.Campaign, normalizedCampaignId, AuditOutcome.Info, "Queue creation skipped because the campaign is not approved or is not active.", new { CampaignId = normalizedCampaignId }, cancellationToken);
            return new CampaignQueueCreationResultDto { CampaignId = normalizedCampaignId };
        }

        var targets = await domainUnitOfWork.Campaigns.ListCampaignTargetsAsync(normalizedCampaignId, cancellationToken);
        if (targets.Count == 0)
        {
            await AddAuditAsync("Phase5QueueCreationNoOp", auditActorId, auditActorRole, AuditTargetType.Campaign, normalizedCampaignId, AuditOutcome.Info, "Queue creation skipped because the campaign has no targets.", new { CampaignId = normalizedCampaignId }, cancellationToken);
            return new CampaignQueueCreationResultDto { CampaignId = normalizedCampaignId };
        }

        var targetDoctorIds = targets.Select(target => target.DoctorId).Distinct(StringComparer.Ordinal).ToArray();
        var eligibleDoctors = await domainUnitOfWork.Profiles.ListEligibleDoctorsByIdsAsync(targetDoctorIds, cancellationToken);
        var eligibleDoctorIds = eligibleDoctors.Select(doctor => doctor.Id).ToHashSet(StringComparer.Ordinal);
        var createdCount = 0;
        var skippedCount = 0;
        var duplicateExistingCount = 0;

        foreach (var target in targets)
        {
            if (await domainUnitOfWork.MessageQueues.QueueItemExistsAsync(normalizedCampaignId, target.DoctorId, cancellationToken))
            {
                duplicateExistingCount++;
                await AddAuditAsync("Phase5QueueCreationDuplicateRetry", auditActorId, auditActorRole, AuditTargetType.Campaign, normalizedCampaignId, AuditOutcome.Info, "Existing queue item preserved during retry.", new { CampaignId = normalizedCampaignId, target.DoctorId }, cancellationToken);
                continue;
            }

            if (!eligibleDoctorIds.Contains(target.DoctorId))
            {
                skippedCount++;
                await AddAuditAsync("Phase5QueueCreationSkippedTarget", auditActorId, auditActorRole, AuditTargetType.Campaign, normalizedCampaignId, AuditOutcome.Info, "Campaign target was no longer eligible at queue creation time.", new { CampaignId = normalizedCampaignId, target.DoctorId }, cancellationToken);
                continue;
            }

            var wasCreated = await domainUnitOfWork.MessageQueues.TryAddQueueItemAsync(new DoctorMessageQueue
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = normalizedCampaignId,
                DoctorId = target.DoctorId,
                QueuedAtUtc = queuedAtUtc,
                CampaignSubmittedAtUtc = campaign.SubmittedAtUtc ?? campaign.CreatedAtUtc,
                Status = QueueItemStatus.Queued,
                CreatedAtUtc = DateTime.UtcNow
            }, cancellationToken);
            if (wasCreated)
            {
                createdCount++;
                continue;
            }

            duplicateExistingCount++;
            await AddAuditAsync("Phase5QueueCreationDuplicateRetry", auditActorId, auditActorRole, AuditTargetType.Campaign, normalizedCampaignId, AuditOutcome.Info, "Existing queue item preserved during concurrent retry.", new { CampaignId = normalizedCampaignId, target.DoctorId }, cancellationToken);
        }

        await AddAuditAsync("Phase5QueueCreationSucceeded", auditActorId, auditActorRole, AuditTargetType.Campaign, normalizedCampaignId, AuditOutcome.Success, "Approved campaign queue creation processed.", new { CampaignId = normalizedCampaignId, CreatedCount = createdCount, SkippedCount = skippedCount, DuplicateExistingCount = duplicateExistingCount }, cancellationToken);
        return new CampaignQueueCreationResultDto
        {
            CampaignId = normalizedCampaignId,
            CreatedCount = createdCount,
            SkippedCount = skippedCount,
            DuplicateExistingCount = duplicateExistingCount
        };
    }

    private async Task ValidateCreateCampaignRequestAsync(string actorUserId, CompanyActor company, CreateCampaignRequestDto request, CancellationToken cancellationToken)
    {
        var validationResult = await createCampaignValidator.ValidateAsync(request, cancellationToken);
        if (validationResult.IsValid)
        {
            return;
        }

        await AddAuditAsync("Phase5CampaignSubmissionValidationFailed", actorUserId, company.ActorRole, AuditTargetType.Company, company.CompanyId, AuditOutcome.Denied, "Campaign content validation failed.", new { ErrorCount = validationResult.Errors.Count }, cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        throw new Phase5ValidationException("Validation failed.", validationResult.Errors.Select(error => error.ErrorMessage).ToArray());
    }

    private async Task<IReadOnlyList<StoredFile>> ValidateAssetsAsync(string actorUserId, CompanyActor company, IReadOnlyList<string> assetIds, CancellationToken cancellationToken)
    {
        var assets = new List<StoredFile>(assetIds.Count);
        foreach (var assetId in assetIds.Distinct(StringComparer.Ordinal))
        {
            var file = await domainUnitOfWork.StoredFiles.FindByIdAsync(assetId, cancellationToken);
            if (file is null ||
                file.UploadStatus != StoredFileUploadStatus.Stored ||
                file.DeletedAtUtc is not null ||
                file.ReplacedByFileId is not null ||
                file.ReviewStatus is not (StoredFileReviewStatus.Pending or StoredFileReviewStatus.Approved) ||
                file.OwnerType != StoredFileOwnerType.Company ||
                file.OwnerId != company.CompanyId ||
                file.Purpose is not (StoredFilePurpose.CampaignMedia or StoredFilePurpose.VoiceNote or StoredFilePurpose.ClinicalResearchAttachment) ||
                file.RelatedCampaignId is not null)
            {
                await AddAuditAsync("Phase5CampaignAssetValidationFailed", actorUserId, company.ActorRole, AuditTargetType.StoredFile, assetId, AuditOutcome.Denied, "Campaign asset validation failed.", new { AssetId = assetId }, cancellationToken);
                await domainUnitOfWork.SaveChangesAsync(cancellationToken);
                throw new Phase5ValidationException("Validation failed.", ["Campaign assets must be active, pending or approved, and owned by the company."]);
            }

            assets.Add(file);
        }

        if (!assets.Any(asset => asset.Purpose == StoredFilePurpose.CampaignMedia))
        {
            throw new Phase5ValidationException("Validation failed.", ["At least one active pending or approved campaign media asset is required."]);
        }

        return assets;
    }

    private async Task<IReadOnlyList<CampaignTargetSnapshotDto>> ValidateTargetsAsync(string actorUserId, CompanyActor company, IReadOnlyList<string> targetDoctorIds, CancellationToken cancellationToken)
    {
        var requestedIds = targetDoctorIds.Distinct(StringComparer.Ordinal).ToArray();
        var eligibleDoctors = await domainUnitOfWork.Profiles.ListEligibleDoctorsByIdsAsync(requestedIds, cancellationToken);
        var eligibleById = eligibleDoctors
            .Where(doctor => requestedIds.Contains(doctor.Id, StringComparer.Ordinal))
            .ToDictionary(doctor => doctor.Id, StringComparer.Ordinal);

        if (eligibleById.Count != requestedIds.Length)
        {
            await AddAuditAsync("Phase5CampaignTargetValidationFailed", actorUserId, company.ActorRole, AuditTargetType.Company, company.CompanyId, AuditOutcome.Denied, "Campaign target validation failed.", new { RequestedCount = requestedIds.Length, EligibleCount = eligibleById.Count }, cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new Phase5ValidationException("Validation failed.", ["All selected target doctors must be eligible and positively priced."]);
        }

        return requestedIds.Select(id =>
        {
            var doctor = eligibleById[id];
            return new CampaignTargetSnapshotDto
            {
                DoctorId = doctor.Id,
                Specialization = doctor.Specialization,
                ExperienceYears = doctor.ExperienceYears,
                Location = doctor.Location,
                ActivityScore = doctor.ActivityScore,
                PricePerMessage = doctor.PricePerMessage ?? 0m
            };
        }).ToArray();
    }

    private async Task ValidateWalletSufficiencyAsync(string actorUserId, CompanyActor company, IReadOnlyList<CampaignTargetSnapshotDto> targets, CancellationToken cancellationToken)
    {
        var total = CampaignDtoMapper.CalculateTargetPriceTotal(targets);
        var balances = await domainUnitOfWork.Wallets.GetActiveWalletBalancesAsync(WalletOwnerType.Company, company.CompanyId, cancellationToken);
        if (balances is null || balances.Value.AvailableBalance < total)
        {
            await AddAuditAsync("Phase5CampaignInsufficientWalletBalance", actorUserId, company.ActorRole, AuditTargetType.Company, company.CompanyId, AuditOutcome.Denied, "Company wallet available balance is insufficient.", new { RequiredAmount = total, AvailableAmount = balances?.AvailableBalance ?? 0m }, cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new Phase5ConflictException("Company wallet available balance is insufficient.");
        }
    }

    private async Task<CampaignDetailDto> LoadCompanyCampaignDetailAsync(string companyId, string campaignId, CancellationToken cancellationToken)
    {
        var campaign = await domainUnitOfWork.Campaigns.FindCompanyCampaignAsync(companyId, campaignId, cancellationToken);
        if (campaign is null)
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var targets = await domainUnitOfWork.Campaigns.ListCampaignTargetsAsync(campaignId, cancellationToken);
        var assets = await domainUnitOfWork.StoredFiles.ListByCampaignAsync(campaignId, null, cancellationToken);
        return CampaignDtoMapper.ToDetail(campaign, assets, targets);
    }

    private static int GetQueueStatusCount(
        IReadOnlyDictionary<QueueItemStatus, int> counts,
        QueueItemStatus status)
        => counts.TryGetValue(status, out var count) ? count : 0;

    private async Task<CompanyActor> ResolveApprovedCompanyActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(actorUserId, cancellationToken);
        if (user is { Role: UserRole.Company, AccountStatus: AccountStatus.Approved, IsDeleted: false } && profile is { IsDeleted: false })
        {
            return new CompanyActor(profile.Id, user.Role.ToString());
        }

        await AddAuditAsync("Phase5CampaignOwnershipDenied", actorUserId, user?.Role.ToString() ?? "Unknown", AuditTargetType.User, actorUserId, AuditOutcome.Denied, "Actor is not an approved active company user.", new { ActorUserId = actorUserId }, cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        throw new Phase5ForbiddenException("Forbidden.");
    }

    private async Task<CompanyActor> ResolveApprovedCompanyActorForUpdateAsync(
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var user = await identityUnitOfWork.Users.FindByIdForUpdateAsync(actorUserId, cancellationToken);
        var profileSnapshot = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(
            actorUserId,
            cancellationToken);
        if (profileSnapshot is null)
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByIdForUpdateAsync(
            profileSnapshot.Id,
            cancellationToken);
        if (user is not { Role: UserRole.Company, AccountStatus: AccountStatus.Approved, IsDeleted: false }
            || profile is not { IsDeleted: false }
            || !string.Equals(profile.UserId, actorUserId, StringComparison.Ordinal))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        return new CompanyActor(profile.Id, user.Role.ToString());
    }

    private async Task<Campaign> FindOwnedCampaignForUpdateAsync(
        string companyId,
        string campaignId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var campaign = await domainUnitOfWork.Campaigns.FindActiveCampaignForUpdateAsync(
            campaignId.Trim(),
            cancellationToken);
        if (campaign is null || !string.Equals(campaign.CompanyId, companyId, StringComparison.Ordinal))
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        return campaign;
    }

    private static void EnsureCompanyEditableCampaign(Campaign campaign)
    {
        if (!CampaignReviewTransitionPolicy.IsCompanyEditableStatus(campaign.Status))
        {
            throw new Phase5ConflictException(
                "Campaign content and assets can only be changed while the campaign is Draft or RevisionRequired.");
        }
    }

    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        var value = idempotencyKey?.Trim() ?? string.Empty;
        if (value.Length is < 8 or > 128)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                ["Idempotency-Key length must be between 8 and 128 characters."]);
        }

        return value;
    }

    private static CampaignDraftDto ToCampaignDraftDto(Campaign campaign)
        => new()
        {
            CampaignId = campaign.Id,
            CompanyId = campaign.CompanyId,
            Title = campaign.Title,
            Description = campaign.Description,
            ClinicalResearchInfo = campaign.ClinicalResearchInfo,
            Status = campaign.Status
        };

    private static CampaignSubmissionDto ToCampaignSubmissionDto(CampaignSubmissionAttempt attempt)
        => new(
            attempt.CampaignId,
            CampaignStatus.PendingReview.ToString(),
            attempt.TargetCount,
            attempt.EstimatedCost,
            attempt.Currency,
            attempt.SubmittedAtUtc);

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidatePagination(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageNumber must be at least 1 and PageSize must be between 1 and 100."]);
        }
    }

    private static string CreateRequestHash(CreateCampaignRequestDto request)
    {
        var normalized = new
        {
            Title = NormalizeRequiredText(request.Title),
            Description = NormalizeRequiredText(request.Description),
            ClinicalResearchInfo = NormalizeRequiredText(request.ClinicalResearchInfo),
            AssetIds = (request.AssetIds ?? Array.Empty<string>()).Select(NormalizeRequiredText).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            TargetDoctorIds = (request.TargetDoctorIds ?? Array.Empty<string>()).Select(NormalizeRequiredText).OrderBy(value => value, StringComparer.Ordinal).ToArray()
        };
        var json = JsonSerializer.Serialize(normalized, HashJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string NormalizeRequiredText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private async Task AddAuditAsync(
        string eventType,
        string? actorUserId,
        string? actorRole,
        AuditTargetType targetType,
        string targetId,
        AuditOutcome outcome,
        string reason,
        object metadata,
        CancellationToken cancellationToken)
    {
        await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            Guid.NewGuid().ToString("N"),
            eventType,
            actorUserId,
            actorRole,
            targetType,
            targetId,
            outcome,
            reason,
            correlationId: null,
            JsonSerializer.Serialize(metadata, HashJsonOptions),
            DateTime.UtcNow,
            cancellationToken);
    }

    private sealed record CompanyActor(string CompanyId, string ActorRole);

    private sealed record SubmissionProcessingResult(CampaignSubmissionResultDto? Result, Phase5WorkflowException? Exception)
    {
        public static SubmissionProcessingResult Success(CampaignSubmissionResultDto result)
        {
            return new SubmissionProcessingResult(result, null);
        }

        public static SubmissionProcessingResult Failure(Phase5WorkflowException exception)
        {
            return new SubmissionProcessingResult(null, exception);
        }
    }
}
