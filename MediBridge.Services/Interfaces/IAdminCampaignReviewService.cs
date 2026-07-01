using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Interfaces;

public interface IAdminCampaignReviewService
{
    Task<PendingCampaignPageDto> ListPendingCampaignsAsync(string adminUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<CampaignReviewDetailDto> GetReviewDetailAsync(string adminUserId, string campaignId, CancellationToken cancellationToken = default);
    Task<CampaignAssetDto> ReviewAssetAsync(string adminUserId, string assetId, ReviewDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<CampaignReviewResultDto> ReviewCampaignAsync(string adminUserId, string campaignId, string idempotencyKey, ReviewDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QueueRowDto>> GetQueueRowsAsync(string adminUserId, string campaignId, CancellationToken cancellationToken = default);
}
