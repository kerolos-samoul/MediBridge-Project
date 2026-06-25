using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Interfaces;

public interface ICampaignWorkflowService
{
    Task<CampaignDto> CreateDraftAsync(string companyUserId, CreateCampaignDraftRequestDto request, CancellationToken cancellationToken = default);
    Task<CampaignAssetDto> UploadAssetAsync(string companyUserId, string campaignId, CampaignAssetUploadRequestDto request, Stream content, CancellationToken cancellationToken = default);
    Task<CampaignAssetDto> ReplaceAssetAsync(string companyUserId, string campaignId, string assetId, CampaignAssetUploadRequestDto request, Stream content, CancellationToken cancellationToken = default);
    Task DeleteAssetAsync(string companyUserId, string campaignId, string assetId, CancellationToken cancellationToken = default);
    Task<TargetPreviewDto> PreviewTargetsAsync(string companyUserId, string campaignId, CancellationToken cancellationToken = default);
    Task<CampaignSubmissionDto> SubmitCampaignAsync(string companyUserId, string campaignId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<QueueSummaryDto> GetQueueSummaryAsync(string companyUserId, string campaignId, CancellationToken cancellationToken = default);
}
