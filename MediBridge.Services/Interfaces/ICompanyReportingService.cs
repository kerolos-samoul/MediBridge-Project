using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Interfaces;

public interface ICompanyReportingService
{
    Task<CampaignReportPageDto> GetCompanyCampaignReportsAsync(
        string actorUserId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? status,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken = default);

    Task<DeliveryReportPageDto> GetCampaignDeliveryReportsAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? status,
        string? state,
        string? doctorSpecialization,
        string? doctorLocation,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken = default);

    Task<FeedbackReportPageDto> GetCampaignFeedbackReportsAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? outcome,
        string? feedbackEligibility,
        string? doctorSpecialization,
        string? doctorLocation,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken = default);

    Task<CampaignAnalyticsDto> GetCampaignAnalyticsAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        CancellationToken cancellationToken = default);
}
