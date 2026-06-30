using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Campaigns;

public sealed class CampaignRepository : ICampaignRepository
{
    private readonly MediBridgeDbContext context;

    public CampaignRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddCampaignAsync(string campaignId, string companyId, CampaignStatus status, CancellationToken cancellationToken = default)
    {
        await context.Campaigns.AddAsync(new Campaign
        {
            Id = campaignId,
            CompanyId = companyId,
            Status = status,
            Title = "Phase 3 campaign",
            Description = "Phase 3 persistence sample"
        }, cancellationToken);
    }

    public async Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        await context.Campaigns.AddAsync(campaign, cancellationToken);
    }

    public Task<Campaign?> FindActiveCampaignAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(campaign => campaign.Id == campaignId && !campaign.IsDeleted, cancellationToken);
    }

    public Task<Campaign?> FindActiveCampaignForUpdateAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns
            .FromSqlInterpolated($"""
                SELECT *
                FROM [Campaigns] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {campaignId}
                    AND [IsDeleted] = CAST(0 AS bit)
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<string?> FindActiveCampaignIdAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns
            .Where(campaign => campaign.Id == campaignId && !campaign.IsDeleted)
            .Select(campaign => campaign.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsActiveDraftCampaignOwnedByCompanyAsync(string campaignId, string companyId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns.AnyAsync(
            campaign => campaign.Id == campaignId &&
                        campaign.CompanyId == companyId &&
                        campaign.Status == CampaignStatus.Draft &&
                        !campaign.IsDeleted,
            cancellationToken);
    }

    public Task<bool> IsCampaignOwnedByCompanyAsync(string campaignId, string companyId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns.AnyAsync(
            campaign => campaign.Id == campaignId && campaign.CompanyId == companyId && !campaign.IsDeleted,
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActiveCampaignIdsByCompanyAsync(string companyId, CancellationToken cancellationToken = default)
    {
        return await context.Campaigns
            .Where(campaign => campaign.CompanyId == companyId && !campaign.IsDeleted)
            .OrderBy(campaign => campaign.CreatedAtUtc)
            .Select(campaign => campaign.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Campaign>> ListCompanyCampaignsAsync(string companyId, CampaignStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Campaigns.Where(campaign => campaign.CompanyId == companyId && !campaign.IsDeleted);
        if (status is not null)
        {
            query = query.Where(campaign => campaign.Status == status);
        }

        return await query
            .OrderByDescending(campaign => campaign.CreatedAtUtc)
            .ThenBy(campaign => campaign.Id)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Max(take, 1))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountCompanyCampaignsAsync(string companyId, CampaignStatus? status, CancellationToken cancellationToken = default)
    {
        var query = context.Campaigns.Where(campaign => campaign.CompanyId == companyId && !campaign.IsDeleted);
        if (status is not null)
        {
            query = query.Where(campaign => campaign.Status == status);
        }

        return query.CountAsync(cancellationToken);
    }

    public Task<Campaign?> FindCompanyCampaignAsync(string companyId, string campaignId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns.FirstOrDefaultAsync(
            campaign => campaign.Id == campaignId && campaign.CompanyId == companyId && !campaign.IsDeleted,
            cancellationToken);
    }

    public Task<Campaign?> FindApprovedCampaignForQueueAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns.FirstOrDefaultAsync(
            campaign => campaign.Id == campaignId && campaign.Status == CampaignStatus.Approved && !campaign.IsDeleted,
            cancellationToken);
    }

    public async Task AddCampaignTargetAsync(string campaignTargetId, string campaignId, string doctorId, CancellationToken cancellationToken = default)
    {
        await context.CampaignTargets.AddAsync(new CampaignTarget
        {
            Id = campaignTargetId,
            CampaignId = campaignId,
            DoctorId = doctorId,
            SpecializationSnapshot = "Cardiology",
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 95m,
            PricePerMessageSnapshot = 50m
        }, cancellationToken);
    }

    public async Task AddCampaignTargetsAsync(IEnumerable<CampaignTarget> targets, CancellationToken cancellationToken = default)
    {
        await context.CampaignTargets.AddRangeAsync(targets, cancellationToken);
    }

    public Task<bool> IsActiveEditableCampaignOwnedByCompanyAsync(
        string campaignId,
        string companyId,
        CancellationToken cancellationToken = default)
    {
        return context.Campaigns.AnyAsync(
            campaign => campaign.Id == campaignId
                && campaign.CompanyId == companyId
                && (campaign.Status == CampaignStatus.Draft
                    || campaign.Status == CampaignStatus.RevisionRequired)
                && !campaign.IsDeleted,
            cancellationToken);
    }

    public async Task AddCampaignTargetAsync(CampaignTarget campaignTarget, CancellationToken cancellationToken = default)
    {
        await context.CampaignTargets.AddAsync(campaignTarget, cancellationToken);
    }

    public async Task ReplaceCampaignTargetsAsync(
        string campaignId,
        IReadOnlyCollection<CampaignTarget> campaignTargets,
        CancellationToken cancellationToken = default)
    {
        var existingTargets = await context.CampaignTargets
            .Where(target => target.CampaignId == campaignId)
            .ToListAsync(cancellationToken);
        context.CampaignTargets.RemoveRange(existingTargets);
        if (campaignTargets.Count > 0)
        {
            await context.CampaignTargets.AddRangeAsync(campaignTargets, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> ListCampaignTargetIdsAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return await context.CampaignTargets
            .Where(target => target.CampaignId == campaignId)
            .OrderBy(target => target.CreatedAtUtc)
            .Select(target => target.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CampaignTarget>> ListCampaignTargetsAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return await context.CampaignTargets
            .Where(target => target.CampaignId == campaignId)
            .OrderBy(target => target.CreatedAtUtc)
            .ThenBy(target => target.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountCampaignTargetsAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return context.CampaignTargets.CountAsync(target => target.CampaignId == campaignId, cancellationToken);
    }

    public Task<CampaignSubmissionRequest?> FindSubmissionRequestAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return context.Set<CampaignSubmissionRequest>()
            .FirstOrDefaultAsync(request => request.CompanyId == companyId && request.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public Task<CampaignSubmissionRequest?> FindSubmissionRequestForUpdateAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return context.Set<CampaignSubmissionRequest>()
            .FromSqlInterpolated($"""
                SELECT *
                FROM [CampaignSubmissionRequests] WITH (UPDLOCK, HOLDLOCK)
                WHERE [CompanyId] = {companyId}
                    AND [IdempotencyKey] = {idempotencyKey}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddSubmissionRequestAsync(CampaignSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        await context.Set<CampaignSubmissionRequest>().AddAsync(request, cancellationToken);
    }

    public async Task AddCampaignReviewHistoryAsync(string reviewHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, CancellationToken cancellationToken = default)
    {
        await context.CampaignReviewHistories.AddAsync(new CampaignReviewHistory
        {
            Id = reviewHistoryId,
            CampaignId = campaignId,
            AdminUserId = adminUserId,
            Decision = decision,
            Notes = "Phase 3 persistence sample"
        }, cancellationToken);
    }

    public async Task AddCampaignReviewHistoryAsync(CampaignReviewHistory history, CancellationToken cancellationToken = default)
    {
        await context.CampaignReviewHistories.AddAsync(history, cancellationToken);
    }

    public Task<CampaignReviewHistory?> FindCampaignReviewByIdempotencyKeyAsync(
        string campaignId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return context.CampaignReviewHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                history => history.CampaignId == campaignId && history.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public async Task AddCampaignReviewHistoryCorrectionAsync(string reviewHistoryId, string correctsHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, string reason, CancellationToken cancellationToken = default)
    {
        await context.CampaignReviewHistories.AddAsync(new CampaignReviewHistory
        {
            Id = reviewHistoryId,
            CampaignId = campaignId,
            AdminUserId = adminUserId,
            Decision = decision,
            Reason = reason,
            CorrectsHistoryId = correctsHistoryId
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListCampaignReviewHistoryIdsAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return await context.CampaignReviewHistories
            .Where(history => history.CampaignId == campaignId)
            .OrderBy(history => history.CreatedAtUtc)
            .Select(history => history.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<PendingCampaignReviewPageReadModel> ListPendingReviewCampaignsAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var boundedSkip = Math.Max(0, skip);
        var boundedTake = Math.Clamp(take, 1, 100);
        var query = context.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.PendingReview
                && campaign.SubmittedAtUtc != null
                && !campaign.IsDeleted)
            .Join(
                context.CompanyProfiles.AsNoTracking(),
                campaign => campaign.CompanyId,
                company => company.Id,
                (campaign, company) => new { Campaign = campaign, Company = company });
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.Campaign.SubmittedAtUtc)
            .ThenBy(row => row.Campaign.Id)
            .Skip(boundedSkip)
            .Take(boundedTake)
            .Select(row => new PendingCampaignReviewReadModel(
                row.Campaign.Id,
                row.Campaign.CompanyId,
                row.Company.CompanyName,
                row.Campaign.Title,
                row.Campaign.Description,
                row.Campaign.Status,
                row.Campaign.SubmittedAtUtc!.Value,
                context.CampaignTargets.Count(target => target.CampaignId == row.Campaign.Id),
                context.StoredFiles.Any(file => file.RelatedCampaignId == row.Campaign.Id
                    && file.Purpose == StoredFilePurpose.CampaignMedia
                    && file.UploadStatus == StoredFileUploadStatus.Stored
                    && file.DeletedAtUtc == null
                    && file.ReplacedByFileId == null
                    && (file.ReviewStatus == StoredFileReviewStatus.Pending
                        || file.ReviewStatus == StoredFileReviewStatus.Approved)),
                context.StoredFiles.Any(file => file.RelatedCampaignId == row.Campaign.Id
                    && file.Purpose == StoredFilePurpose.CampaignMedia
                    && file.UploadStatus == StoredFileUploadStatus.Stored
                    && file.DeletedAtUtc == null
                    && file.ReplacedByFileId == null
                    && file.ReviewStatus == StoredFileReviewStatus.Approved)))
            .ToListAsync(cancellationToken);

        return new PendingCampaignReviewPageReadModel(items, totalCount);
    }

    public Task<CampaignReviewDetailReadModel?> FindPendingReviewDetailAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return context.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Id == campaignId
                && campaign.Status == CampaignStatus.PendingReview
                && campaign.SubmittedAtUtc != null
                && !campaign.IsDeleted)
            .Join(
                context.CompanyProfiles.AsNoTracking(),
                campaign => campaign.CompanyId,
                company => company.Id,
                (campaign, company) => new CampaignReviewDetailReadModel(
                    campaign.Id,
                    campaign.CompanyId,
                    company.CompanyName,
                    campaign.Title,
                    campaign.Description,
                    campaign.ClinicalResearchInfo,
                    campaign.Status,
                    campaign.SubmittedAtUtc!.Value,
                    context.CampaignTargets.Count(target => target.CampaignId == campaign.Id),
                    context.StoredFiles.Any(file => file.RelatedCampaignId == campaign.Id
                        && file.Purpose == StoredFilePurpose.CampaignMedia
                        && file.UploadStatus == StoredFileUploadStatus.Stored
                        && file.DeletedAtUtc == null
                        && file.ReplacedByFileId == null
                        && (file.ReviewStatus == StoredFileReviewStatus.Pending
                            || file.ReviewStatus == StoredFileReviewStatus.Approved)),
                    context.StoredFiles.Any(file => file.RelatedCampaignId == campaign.Id
                        && file.Purpose == StoredFilePurpose.CampaignMedia
                        && file.UploadStatus == StoredFileUploadStatus.Stored
                        && file.DeletedAtUtc == null
                        && file.ReplacedByFileId == null
                        && file.ReviewStatus == StoredFileReviewStatus.Approved)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<CampaignReviewHistory?> FindLatestCampaignReviewHistoryAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return context.CampaignReviewHistories
            .AsNoTracking()
            .Where(history => history.CampaignId == campaignId)
            .OrderByDescending(history => history.CreatedAtUtc)
            .ThenByDescending(history => history.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddCampaignSubmissionAttemptAsync(
        CampaignSubmissionAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        await context.CampaignSubmissionAttempts.AddAsync(attempt, cancellationToken);
    }

    public Task<CampaignSubmissionAttempt?> FindCampaignSubmissionAttemptByKeyAsync(
        string campaignId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return context.CampaignSubmissionAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                attempt => attempt.CampaignId == campaignId && attempt.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public Task<CampaignSubmissionAttempt?> FindCurrentCampaignSubmissionAttemptAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return context.CampaignSubmissionAttempts
            .AsNoTracking()
            .Where(attempt => attempt.CampaignId == campaignId)
            .OrderByDescending(attempt => attempt.SubmittedAtUtc)
            .ThenByDescending(attempt => attempt.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<int> CountCampaignQueueItemsAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return context.DoctorMessageQueues
            .AsNoTracking()
            .CountAsync(
                queue => queue.CampaignId == campaignId
                    && (queue.Status == QueueItemStatus.Queued || queue.Status == QueueItemStatus.Activated),
                cancellationToken);
    }

    public async Task<IReadOnlyList<CampaignQueueRowReadModel>> ListCampaignQueueRowsAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.CampaignId == campaignId)
            .OrderBy(queue => queue.CampaignSubmittedAtUtc)
            .ThenBy(queue => queue.QueuedAtUtc)
            .ThenBy(queue => queue.Id)
            .Select(queue => new CampaignQueueRowReadModel(
                queue.Id,
                queue.CampaignId,
                queue.DoctorId,
                queue.Status,
                queue.CampaignSubmittedAtUtc,
                queue.QueuedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<string?> FindCampaignIdIncludingDeletedAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        return context.Campaigns
            .IgnoreQueryFilters()
            .Where(campaign => campaign.Id == campaignId)
            .Select(campaign => campaign.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
