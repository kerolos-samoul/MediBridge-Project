using MediBridge.Core.Interfaces.Campaigns;

namespace MediBridge.Services.DTOs.Campaigns;

public static class CampaignReportingDtoMapper
{
    public static CampaignReportPageDto ToCampaignReportPage(
        CompanyReportingPageReadModel<CampaignReportSummaryReadModel> page,
        CompanyReportingPagination pagination)
        => new()
        {
            Page = ToPageMetadata(page.TotalCount, pagination),
            Items = page.Items.Select(ToCampaignReportSummary).ToArray()
        };

    public static CampaignReportSummaryDto ToCampaignReportSummary(CampaignReportSummaryReadModel model)
        => new()
        {
            CampaignId = model.CampaignId,
            Title = model.Title,
            Status = model.Status,
            SubmittedAtUtc = model.SubmittedAtUtc,
            ReviewedAtUtc = model.ReviewedAtUtc,
            TargetCount = model.TargetCount,
            DeliveredCount = model.DeliveredCount,
            ActiveUnansweredCount = model.ActiveUnansweredCount,
            AcceptedCount = model.AcceptedCount,
            RejectedCount = model.RejectedCount,
            ExpiredCount = model.ExpiredCount,
            FeedbackCount = model.FeedbackCount,
            ReservedAmount = model.ReservedAmount,
            ChargedSpend = model.ChargedSpend,
            DoctorEarnings = model.DoctorEarnings,
            PlatformFee = model.PlatformFee
        };

    public static DeliveryReportPageDto ToDeliveryReportPage(
        CompanyReportingPageReadModel<CampaignDeliveryReportRowReadModel> page,
        CompanyReportingPagination pagination)
        => new()
        {
            Page = ToPageMetadata(page.TotalCount, pagination),
            Items = page.Items.Select(ToDeliveryReportRow).ToArray()
        };

    public static CampaignDeliveryReportRowDto ToDeliveryReportRow(CampaignDeliveryReportRowReadModel model)
        => new()
        {
            DeliveryId = model.DeliveryId,
            CampaignId = model.CampaignId,
            PublicDoctorId = model.Doctor.PublicDoctorId,
            DoctorSpecialization = model.Doctor.Specialization,
            DoctorExperienceBand = model.Doctor.ExperienceBand,
            DoctorLocation = model.Doctor.Location,
            DeliveryDateEgypt = model.DeliveryDateEgypt,
            DeliveredAtUtc = model.DeliveredAtUtc,
            ReadAtUtc = model.ReadAtUtc,
            Status = model.Status,
            InteractedAtUtc = model.InteractedAtUtc,
            PriceSnapshot = model.PriceSnapshot,
            ReservedAmount = model.ReservedAmount,
            ChargedAmount = model.ChargedAmount,
            DoctorEarnings = model.DoctorEarnings,
            PlatformFee = model.PlatformFee
        };

    public static FeedbackReportPageDto ToFeedbackReportPage(
        CompanyReportingPageReadModel<CampaignFeedbackReportRowReadModel> page,
        CompanyReportingPagination pagination)
        => new()
        {
            Page = ToPageMetadata(page.TotalCount, pagination),
            Items = page.Items.Select(ToFeedbackReportRow).ToArray()
        };

    public static CampaignFeedbackReportRowDto ToFeedbackReportRow(CampaignFeedbackReportRowReadModel model)
        => new()
        {
            DeliveryId = model.DeliveryId,
            CampaignId = model.CampaignId,
            Outcome = model.Outcome.ToString(),
            FeedbackText = model.FeedbackText,
            FeedbackCreatedAtUtc = model.FeedbackCreatedAtUtc,
            FeedbackQualifiesForScore = model.FeedbackQualifiesForScore,
            PublicDoctorId = model.Doctor.PublicDoctorId,
            DoctorSpecialization = model.Doctor.Specialization,
            DoctorExperienceBand = model.Doctor.ExperienceBand,
            DoctorLocation = model.Doctor.Location
        };

    public static CampaignAnalyticsDto ToAnalytics(CampaignAnalyticsReadModel model)
        => new()
        {
            CampaignId = model.CampaignId,
            FromDateEgypt = model.FromDateEgypt,
            ToDateEgypt = model.ToDateEgypt,
            DeliveredCount = model.DeliveredCount,
            ActiveUnansweredCount = model.ActiveUnansweredCount,
            AcceptedCount = model.AcceptedCount,
            RejectedCount = model.RejectedCount,
            ExpiredCount = model.ExpiredCount,
            FeedbackCount = model.FeedbackCount,
            InteractionRate = ToRateMetric(model.InteractionRate),
            AcceptanceRate = ToRateMetric(model.AcceptanceRate),
            RejectionRate = ToRateMetric(model.RejectionRate),
            ExpiryRate = ToRateMetric(model.ExpiryRate),
            FeedbackRate = ToRateMetric(model.FeedbackRate),
            ReservedAmount = model.ReservedAmount,
            ChargedSpend = model.ChargedSpend,
            DoctorEarnings = model.DoctorEarnings,
            PlatformFee = model.PlatformFee
        };

    public static PublicDoctorSummaryDto ToPublicDoctorSummary(PublicDoctorSummaryReadModel model)
        => new()
        {
            PublicDoctorId = model.PublicDoctorId,
            Specialization = model.Specialization,
            ExperienceBand = model.ExperienceBand,
            Location = model.Location
        };

    public static RateMetricReadModel CreateRateMetric(int numerator, int denominator)
    {
        if (numerator < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "Rate numerator cannot be negative.");
        }

        if (denominator < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Rate denominator cannot be negative.");
        }

        if (denominator == 0)
        {
            return new RateMetricReadModel(0m, numerator, denominator, HasEligibleRecords: false);
        }

        var value = Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);
        return new RateMetricReadModel(value, numerator, denominator, HasEligibleRecords: true);
    }

    public static RateMetricDto ToRateMetric(RateMetricReadModel model)
        => new()
        {
            Value = model.Value,
            Numerator = model.Numerator,
            Denominator = model.Denominator,
            HasEligibleRecords = model.HasEligibleRecords
        };

    private static ReportingPageMetadataDto ToPageMetadata(int totalCount, CompanyReportingPagination pagination)
        => new()
        {
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
}
