namespace MediBridge.Services.DTOs.Campaigns;

public sealed record CampaignAssetUploadRequestDto(
    string OriginalFileName,
    string ContentType,
    long SizeBytes);
