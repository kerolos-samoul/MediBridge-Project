using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed class CreateCampaignRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ClinicalResearchInfo { get; set; }
    public IReadOnlyList<string> AssetIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetDoctorIds { get; set; } = Array.Empty<string>();
}

public sealed class CampaignAssetDto
{
    public string FileId { get; set; } = string.Empty;
    public StoredFilePurpose Purpose { get; set; }
    public StoredFileReviewStatus ReviewStatus { get; set; }
}

public sealed class CampaignTargetSnapshotDto
{
    public string DoctorId { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public string Location { get; set; } = string.Empty;
    public decimal ActivityScore { get; set; }
    public decimal PricePerMessage { get; set; }
}

public sealed class CampaignSummaryDto
{
    public string CampaignId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CampaignStatus Status { get; set; }
    public int TargetCount { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}

public sealed class CampaignDetailDto
{
    public string CampaignId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CampaignStatus Status { get; set; }
    public int TargetCount { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ClinicalResearchInfo { get; set; } = string.Empty;
    public IReadOnlyList<CampaignAssetDto> Assets { get; set; } = Array.Empty<CampaignAssetDto>();
    public IReadOnlyList<CampaignTargetSnapshotDto> Targets { get; set; } = Array.Empty<CampaignTargetSnapshotDto>();
}

public sealed class CampaignPageDto
{
    public CampaignPageMetadataDto Page { get; set; } = new();
    public IReadOnlyList<CampaignSummaryDto> Items { get; set; } = Array.Empty<CampaignSummaryDto>();
}

public sealed class CampaignPageMetadataDto
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed class CampaignQueueCreationResultDto
{
    public string CampaignId { get; set; } = string.Empty;
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int DuplicateExistingCount { get; set; }
}

public sealed class CampaignSubmissionResultDto
{
    public CampaignDetailDto Campaign { get; set; } = new();
    public bool IsIdempotentReplay { get; set; }
}
