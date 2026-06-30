using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Campaigns;

public sealed record PendingCampaignReviewReadModel(
    string CampaignId,
    string CompanyId,
    string CompanyName,
    string Title,
    string Description,
    CampaignStatus Status,
    DateTime SubmittedAtUtc,
    int TargetCount,
    bool HasReviewableMedia,
    bool HasApprovedMedia);

public sealed record PendingCampaignReviewPageReadModel(
    IReadOnlyList<PendingCampaignReviewReadModel> Items,
    int TotalCount);

public sealed record CampaignReviewDetailReadModel(
    string CampaignId,
    string CompanyId,
    string CompanyName,
    string Title,
    string Description,
    string? ClinicalResearchInfo,
    CampaignStatus Status,
    DateTime SubmittedAtUtc,
    int TargetCount,
    bool HasReviewableMedia,
    bool HasApprovedMedia);

public sealed record CampaignQueueRowReadModel(
    string QueueId,
    string CampaignId,
    string DoctorId,
    QueueItemStatus Status,
    DateTime? CampaignSubmittedAtUtc,
    DateTime QueuedAtUtc);
