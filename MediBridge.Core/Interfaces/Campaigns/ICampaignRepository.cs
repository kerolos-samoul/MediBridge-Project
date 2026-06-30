using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Campaigns;

public interface ICampaignRepository
{
    Task AddCampaignAsync(string campaignId, string companyId, CampaignStatus status, CancellationToken cancellationToken = default);
    Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task<Campaign?> FindActiveCampaignAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<Campaign?> FindActiveCampaignForUpdateAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<string?> FindActiveCampaignIdAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveDraftCampaignOwnedByCompanyAsync(string campaignId, string companyId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveEditableCampaignOwnedByCompanyAsync(string campaignId, string companyId, CancellationToken cancellationToken = default);
    Task<bool> IsCampaignOwnedByCompanyAsync(string campaignId, string companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActiveCampaignIdsByCompanyAsync(string companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Campaign>> ListCompanyCampaignsAsync(string companyId, CampaignStatus? status, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountCompanyCampaignsAsync(string companyId, CampaignStatus? status, CancellationToken cancellationToken = default);
    Task<Campaign?> FindCompanyCampaignAsync(string companyId, string campaignId, CancellationToken cancellationToken = default);
    Task<Campaign?> FindApprovedCampaignForQueueAsync(string campaignId, CancellationToken cancellationToken = default);
    Task AddCampaignTargetAsync(string campaignTargetId, string campaignId, string doctorId, CancellationToken cancellationToken = default);
    Task AddCampaignTargetAsync(CampaignTarget campaignTarget, CancellationToken cancellationToken = default);
    Task AddCampaignTargetsAsync(IEnumerable<CampaignTarget> targets, CancellationToken cancellationToken = default);
    Task ReplaceCampaignTargetsAsync(string campaignId, IReadOnlyCollection<CampaignTarget> campaignTargets, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListCampaignTargetIdsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CampaignTarget>> ListCampaignTargetsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<int> CountCampaignTargetsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<CampaignSubmissionRequest?> FindSubmissionRequestAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CampaignSubmissionRequest?> FindSubmissionRequestForUpdateAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task AddSubmissionRequestAsync(CampaignSubmissionRequest request, CancellationToken cancellationToken = default);
    Task AddCampaignReviewHistoryAsync(string reviewHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, CancellationToken cancellationToken = default);
    Task AddCampaignReviewHistoryAsync(CampaignReviewHistory history, CancellationToken cancellationToken = default);
    Task<CampaignReviewHistory?> FindCampaignReviewByIdempotencyKeyAsync(string campaignId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task AddCampaignReviewHistoryCorrectionAsync(string reviewHistoryId, string correctsHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, string reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListCampaignReviewHistoryIdsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<PendingCampaignReviewPageReadModel> ListPendingReviewCampaignsAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<CampaignReviewDetailReadModel?> FindPendingReviewDetailAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<CampaignReviewHistory?> FindLatestCampaignReviewHistoryAsync(string campaignId, CancellationToken cancellationToken = default);
    Task AddCampaignSubmissionAttemptAsync(CampaignSubmissionAttempt attempt, CancellationToken cancellationToken = default);
    Task<CampaignSubmissionAttempt?> FindCampaignSubmissionAttemptByKeyAsync(string campaignId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CampaignSubmissionAttempt?> FindCurrentCampaignSubmissionAttemptAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<int> CountCampaignQueueItemsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CampaignQueueRowReadModel>> ListCampaignQueueRowsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<string?> FindCampaignIdIncludingDeletedAsync(string campaignId, CancellationToken cancellationToken = default);
}
