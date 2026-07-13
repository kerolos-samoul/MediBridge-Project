using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Campaigns;

public sealed record PublicDoctorSummaryReadModel(
    string PublicDoctorId,
    string Specialization,
    string ExperienceBand,
    string Location);

public sealed record CompanyReportingPageReadModel<T>(
    IReadOnlyList<T> Items,
    int TotalCount);

public sealed record CampaignReportSummaryReadModel(
    string CampaignId,
    string Title,
    CampaignStatus Status,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    int TargetCount,
    int DeliveredCount,
    int ActiveUnansweredCount,
    int AcceptedCount,
    int RejectedCount,
    int ExpiredCount,
    int FeedbackCount,
    decimal ReservedAmount,
    decimal ChargedSpend,
    decimal DoctorEarnings,
    decimal PlatformFee,
    DateTime CampaignActivityAtUtc);

public sealed record CampaignDeliveryReportRowReadModel(
    string DeliveryId,
    string CampaignId,
    PublicDoctorSummaryReadModel Doctor,
    DateOnly DeliveryDateEgypt,
    DateTime DeliveredAtUtc,
    DateTime? ReadAtUtc,
    DeliveryStatus Status,
    DateTime? InteractedAtUtc,
    decimal PriceSnapshot,
    decimal ReservedAmount,
    decimal ChargedAmount,
    decimal DoctorEarnings,
    decimal PlatformFee);

public sealed record CampaignFeedbackReportRowReadModel(
    string DeliveryId,
    string CampaignId,
    CompanyReportingFeedbackOutcome Outcome,
    string FeedbackText,
    DateTime FeedbackCreatedAtUtc,
    bool FeedbackQualifiesForScore,
    PublicDoctorSummaryReadModel Doctor);

public sealed record RateMetricReadModel(
    decimal Value,
    int Numerator,
    int Denominator,
    bool HasEligibleRecords);

public sealed record CampaignAnalyticsReadModel(
    string CampaignId,
    DateOnly FromDateEgypt,
    DateOnly ToDateEgypt,
    int DeliveredCount,
    int ActiveUnansweredCount,
    int AcceptedCount,
    int RejectedCount,
    int ExpiredCount,
    int FeedbackCount,
    RateMetricReadModel InteractionRate,
    RateMetricReadModel AcceptanceRate,
    RateMetricReadModel RejectionRate,
    RateMetricReadModel ExpiryRate,
    RateMetricReadModel FeedbackRate,
    decimal ReservedAmount,
    decimal ChargedSpend,
    decimal DoctorEarnings,
    decimal PlatformFee);

public sealed record CompanyReportingDeliveryAggregateReadModel(
    string CampaignId,
    int DeliveredCount,
    int ActiveUnansweredCount,
    int AcceptedCount,
    int RejectedCount,
    int ExpiredCount,
    int FeedbackCount,
    decimal ReservedAmount,
    decimal ChargedSpend,
    decimal DoctorEarnings,
    decimal PlatformFee,
    DateTime? LatestDeliveredAtUtc,
    DateTime? LatestReadAtUtc,
    DateTime? LatestInteractedAtUtc,
    DateTime? LatestFeedbackCreatedAtUtc);

public sealed record CompanyReportingDeliverySourceReadModel(
    string DeliveryId,
    string CampaignId,
    DeliveryStatus Status,
    ReservationStatus ReservationStatus,
    DateOnly DeliveryDateEgypt,
    decimal ReservedAmount,
    decimal PriceSnapshot,
    decimal DoctorEarnings,
    decimal PlatformFee,
    bool HasFeedback);

public sealed record CompanyReportingFinancialEvidenceReadModel(
    string DeliveryId,
    decimal ReservedAmount,
    decimal ReleasedAmount,
    decimal ChargedAmount,
    decimal EarnedAmount,
    decimal PlatformFeeAmount);

public sealed record CompanyReportingReconciliationReadModel(
    bool IsConsistent,
    ReportingDiscrepancyCategory? Category,
    string ReportKind,
    string CampaignId,
    DateOnly FromDateEgypt,
    DateOnly ToDateEgypt,
    decimal ExpectedAmount,
    decimal ActualAmount,
    int AffectedDeliveryCount);
