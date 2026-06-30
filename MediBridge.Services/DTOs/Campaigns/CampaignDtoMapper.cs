using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;

namespace MediBridge.Services.DTOs.Campaigns;

public static class CampaignDtoMapper
{
    public static CampaignSummaryDto ToSummary(Campaign campaign, int targetCount)
    {
        return new CampaignSummaryDto
        {
            CampaignId = campaign.Id,
            Title = campaign.Title,
            Status = campaign.Status,
            TargetCount = targetCount,
            SubmittedAtUtc = campaign.SubmittedAtUtc ?? campaign.CreatedAtUtc
        };
    }

    public static CampaignDetailDto ToDetail(Campaign campaign, IReadOnlyList<StoredFile> assets, IReadOnlyList<CampaignTarget> targets)
    {
        return new CampaignDetailDto
        {
            CampaignId = campaign.Id,
            Title = campaign.Title,
            Status = campaign.Status,
            TargetCount = targets.Count,
            SubmittedAtUtc = campaign.SubmittedAtUtc ?? campaign.CreatedAtUtc,
            Description = campaign.Description,
            ClinicalResearchInfo = campaign.ClinicalResearchInfo ?? string.Empty,
            Assets = assets.Select(ToAsset).ToArray(),
            Targets = targets.Select(ToTargetSnapshot).ToArray()
        };
    }

    public static CampaignAssetDto ToAsset(StoredFile file)
    {
        return new CampaignAssetDto
        {
            FileId = file.Id,
            Purpose = file.Purpose,
            ReviewStatus = file.ReviewStatus
        };
    }

    public static CampaignTargetSnapshotDto ToTargetSnapshot(CampaignTarget target)
    {
        return new CampaignTargetSnapshotDto
        {
            DoctorId = target.DoctorId,
            Specialization = target.SpecializationSnapshot,
            ExperienceYears = target.ExperienceYearsSnapshot,
            Location = target.LocationSnapshot,
            ActivityScore = target.ActivityScoreSnapshot,
            PricePerMessage = target.PricePerMessageSnapshot
        };
    }

    public static decimal CalculateTargetPriceTotal(IEnumerable<CampaignTargetSnapshotDto> targets)
    {
        return targets.Sum(target => target.PricePerMessage);
    }
}
