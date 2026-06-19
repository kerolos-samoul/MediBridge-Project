using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Interfaces;

public interface ICampaignWorkflowService
{
    Task<CampaignSubmissionResultDto> SubmitCampaignAsync(string actorUserId, string? idempotencyKey, CreateCampaignRequestDto request, CancellationToken cancellationToken = default);

    Task<CampaignPageDto> GetCompanyCampaignsAsync(string actorUserId, CampaignStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    Task<CampaignDetailDto> GetCompanyCampaignDetailAsync(string actorUserId, string campaignId, CancellationToken cancellationToken = default);

    Task<CampaignQueueCreationResultDto> CreateQueueForApprovedCampaignAsync(string campaignId, DateTime queuedAtUtc, string? actorUserId = null, CancellationToken cancellationToken = default);
}
