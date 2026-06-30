using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Interfaces;

public interface ICampaignDraftService
{
    Task<CampaignDraftDto> CreateDraftAsync(
        string actorUserId,
        CreateCampaignDraftRequestDto request,
        CancellationToken cancellationToken = default);
}
