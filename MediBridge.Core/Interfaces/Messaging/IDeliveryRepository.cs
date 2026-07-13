using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;

namespace MediBridge.Core.Interfaces.Messaging;

public interface IDeliveryRepository
{
    Task AddDeliveryAsync(
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly deliveryDateEgypt,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        DateTime deliveredAtUtc,
        CancellationToken cancellationToken = default);

    Task<string?> FindDeliveryIdAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default);
    Task<DeliveryReservationReplayReadModel?> FindReservationReplayAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default);
    Task<bool> DeliveryExistsAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListDeliveryHistoryIdsAsync(string doctorId, DateOnly? fromDateEgypt = null, DateOnly? toDateEgypt = null, DeliveryStatus? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OverdueDeliveryReadModel>> ListOverduePageAsync(DateOnly currentBusinessDateEgypt, OverdueDeliveryCursor? after, int take, CancellationToken cancellationToken = default);
    Task<DoctorAdDelivery?> FindActiveReservedForUpdateAsync(string deliveryId, CancellationToken cancellationToken = default);
    Task<int> CountForDoctorOnDateAsync(string doctorId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default);
    Task<bool> HasOverdueActiveReservedForCompanyAsync(string companyId, DateOnly currentBusinessDateEgypt, CancellationToken cancellationToken = default);
    Task<bool> TryMarkExpiredAndReleasedAsync(string deliveryId, DateTime expiredAtUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodayDeliveryReadModel>> ListTodayPageAsync(string doctorId, DateOnly businessDateEgypt, TodayDeliveryCursor? after, int takePlusOne, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovedDeliveryAssetReadModel>> ListApprovedAssetsAsync(IReadOnlyCollection<string> campaignIds, CancellationToken cancellationToken = default);
    Task<DeliveryAssetAuthorizationReadModel?> FindDeliveryAssetAuthorizationAsync(string doctorId, string deliveryId, string fileId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default);
    Task<DoctorAdDelivery?> FindOwnedCurrentDayForReadAsync(string doctorId, string deliveryId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default);
    Task<ReadTrackingReplayReadModel?> TryMarkReadAsync(string deliveryId, DateTime readAtUtc, CancellationToken cancellationToken = default);
    Task<DoctorAdDelivery?> FindOwnedActiveReservedForInteractionAsync(string doctorId, string deliveryId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default);
    Task<InteractionReplayReadModel?> FindSettledInteractionReplayAsync(string doctorId, string deliveryId, CancellationToken cancellationToken = default);
    Task<bool> TryMarkInteractedAndChargedAsync(string deliveryId, DeliveryInteractionOutcome outcome, DateTime interactedAtUtc, string? feedbackText, FeedbackQualityStatus? feedbackQualityStatus, CancellationToken cancellationToken = default);
    Task<ActivityScoreAggregateReadModel> GetActivityScoreAggregateAsync(string doctorId, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 activity score aggregates are not available.");
    Task<WeeklyInteractionCountReadModel> GetWeeklyInteractionCountAsync(string doctorId, DateOnly weekStartDateEgypt, DateOnly weekEndDateEgypt, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 weekly interaction counts are not available.");
    Task<IReadOnlyList<CompanyReportingDeliveryAggregateReadModel>> ListCompanyCampaignDeliveryAggregatesAsync(
        string companyId,
        IReadOnlyCollection<string> campaignIds,
        CompanyReportingDateRange dateRange,
        CancellationToken cancellationToken = default);
    Task<CompanyReportingPageReadModel<CampaignDeliveryReportRowReadModel>> ListCompanyCampaignDeliveryReportsAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CompanyReportingDeliveryFilters filters,
        CompanyReportingPagination pagination,
        CancellationToken cancellationToken = default);
    Task<CompanyReportingPageReadModel<CampaignFeedbackReportRowReadModel>> ListCompanyCampaignFeedbackReportsAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CompanyReportingFeedbackFilters filters,
        CompanyReportingPagination pagination,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyReportingDeliverySourceReadModel>> ListCompanyCampaignAnalyticsDeliveriesAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListCompanyCampaignDeliveryIdsAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CancellationToken cancellationToken = default);
}
