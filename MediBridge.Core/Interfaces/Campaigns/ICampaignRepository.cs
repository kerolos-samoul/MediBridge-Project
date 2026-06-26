using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Campaigns;

public interface ICampaignRepository
{
    Task AddCampaignAsync(string campaignId, string companyId, CampaignStatus status, CancellationToken cancellationToken = default);
    Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task<string?> FindActiveCampaignIdAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveDraftCampaignOwnedByCompanyAsync(string campaignId, string companyId, CancellationToken cancellationToken = default);
    Task<bool> IsCampaignOwnedByCompanyAsync(string campaignId, string companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActiveCampaignIdsByCompanyAsync(string companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Campaign>> ListCompanyCampaignsAsync(string companyId, CampaignStatus? status, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountCompanyCampaignsAsync(string companyId, CampaignStatus? status, CancellationToken cancellationToken = default);
    Task<Campaign?> FindCompanyCampaignAsync(string companyId, string campaignId, CancellationToken cancellationToken = default);
    Task<Campaign?> FindApprovedCampaignForQueueAsync(string campaignId, CancellationToken cancellationToken = default);
    Task AddCampaignTargetAsync(string campaignTargetId, string campaignId, string doctorId, CancellationToken cancellationToken = default);
    Task AddCampaignTargetsAsync(IEnumerable<CampaignTarget> targets, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListCampaignTargetIdsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CampaignTarget>> ListCampaignTargetsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<int> CountCampaignTargetsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<CampaignSubmissionRequest?> FindSubmissionRequestAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CampaignSubmissionRequest?> FindSubmissionRequestForUpdateAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task AddSubmissionRequestAsync(CampaignSubmissionRequest request, CancellationToken cancellationToken = default);
    Task AddCampaignReviewHistoryAsync(string reviewHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, CancellationToken cancellationToken = default);
    Task AddCampaignReviewHistoryCorrectionAsync(string reviewHistoryId, string correctsHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, string reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListCampaignReviewHistoryIdsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<string?> FindCampaignIdIncludingDeletedAsync(string campaignId, CancellationToken cancellationToken = default);
}
