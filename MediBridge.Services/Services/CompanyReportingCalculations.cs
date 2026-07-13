using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Services;

public static class CompanyReportingCalculations
{
    public static CampaignAnalyticsReadModel BuildAnalytics(
        string campaignId,
        CompanyReportingDateRange dateRange,
        IReadOnlyCollection<CompanyReportingDeliverySourceReadModel> deliveries)
    {
        var delivered = deliveries.Count;
        var active = deliveries.Count(delivery => delivery.Status == DeliveryStatus.Active);
        var accepted = deliveries.Count(delivery => delivery.Status == DeliveryStatus.Accepted);
        var rejected = deliveries.Count(delivery => delivery.Status == DeliveryStatus.Rejected);
        var expired = deliveries.Count(delivery => delivery.Status == DeliveryStatus.Expired);
        var interacted = accepted + rejected;
        var feedback = deliveries.Count(delivery => delivery.HasFeedback);

        return new CampaignAnalyticsReadModel(
            campaignId,
            dateRange.FromDateEgypt,
            dateRange.ToDateEgypt,
            delivered,
            active,
            accepted,
            rejected,
            expired,
            feedback,
            CampaignReportingDtoMapper.CreateRateMetric(interacted, delivered),
            CampaignReportingDtoMapper.CreateRateMetric(accepted, interacted),
            CampaignReportingDtoMapper.CreateRateMetric(rejected, interacted),
            CampaignReportingDtoMapper.CreateRateMetric(expired, delivered),
            CampaignReportingDtoMapper.CreateRateMetric(feedback, interacted),
            deliveries.Where(delivery => delivery.Status == DeliveryStatus.Active).Sum(delivery => delivery.ReservedAmount),
            deliveries.Where(IsBillable).Sum(delivery => delivery.PriceSnapshot),
            deliveries.Where(IsBillable).Sum(delivery => delivery.DoctorEarnings),
            deliveries.Where(IsBillable).Sum(delivery => delivery.PlatformFee));
    }

    public static CompanyReportingReconciliationReadModel ClassifySummaryCountReconciliation(
        CampaignReportSummaryReadModel summary,
        CompanyReportingDateRange dateRange)
    {
        var statusTotal = summary.ActiveUnansweredCount + summary.AcceptedCount + summary.RejectedCount + summary.ExpiredCount;
        var isConsistent = summary.DeliveredCount == statusTotal;
        return new CompanyReportingReconciliationReadModel(
            isConsistent,
            isConsistent ? null : ReportingDiscrepancyCategory.CountMismatch,
            "Summary",
            summary.CampaignId,
            dateRange.FromDateEgypt,
            dateRange.ToDateEgypt,
            summary.DeliveredCount,
            statusTotal,
            Math.Abs(summary.DeliveredCount - statusTotal));
    }

    public static CompanyReportingReconciliationReadModel ClassifyFinancialReconciliation(
        string campaignId,
        CompanyReportingDateRange dateRange,
        IReadOnlyCollection<CompanyReportingDeliverySourceReadModel> deliveries,
        IReadOnlyCollection<CompanyReportingFinancialEvidenceReadModel> evidence)
    {
        var billableDeliveryIds = deliveries
            .Where(IsBillable)
            .Select(delivery => delivery.DeliveryId)
            .ToHashSet(StringComparer.Ordinal);
        var activeDeliveryIds = deliveries
            .Where(delivery => delivery.Status == DeliveryStatus.Active)
            .Select(delivery => delivery.DeliveryId)
            .ToHashSet(StringComparer.Ordinal);
        var expectedCharge = deliveries.Where(IsBillable).Sum(delivery => delivery.PriceSnapshot);
        var expectedEarn = deliveries.Where(IsBillable).Sum(delivery => delivery.DoctorEarnings);
        var expectedFee = deliveries.Where(IsBillable).Sum(delivery => delivery.PlatformFee);
        var expectedReserve = deliveries.Where(delivery => delivery.Status == DeliveryStatus.Active).Sum(delivery => delivery.ReservedAmount);
        var scopedEvidence = evidence.Where(item => billableDeliveryIds.Contains(item.DeliveryId)).ToArray();
        var reserveEvidence = evidence.Where(item => activeDeliveryIds.Contains(item.DeliveryId)).ToArray();

        var actualReserve = reserveEvidence.Sum(item => item.ReservedAmount);
        if (actualReserve != expectedReserve)
        {
            return Failed(ReportingDiscrepancyCategory.MissingEvidence, expectedReserve, actualReserve, activeDeliveryIds.Count);
        }

        if (billableDeliveryIds.Count > 0 && scopedEvidence.Length == 0)
        {
            return Failed(ReportingDiscrepancyCategory.MissingEvidence, expectedCharge, 0m, billableDeliveryIds.Count);
        }

        var actualCharge = scopedEvidence.Sum(item => item.ChargedAmount);
        if (actualCharge != expectedCharge)
        {
            return Failed(ReportingDiscrepancyCategory.ChargeMismatch, expectedCharge, actualCharge, billableDeliveryIds.Count);
        }

        var actualEarn = scopedEvidence.Sum(item => item.EarnedAmount);
        if (actualEarn != expectedEarn)
        {
            return Failed(ReportingDiscrepancyCategory.EarnMismatch, expectedEarn, actualEarn, billableDeliveryIds.Count);
        }

        var actualFee = scopedEvidence.Sum(item => item.PlatformFeeAmount);
        if (actualFee != expectedFee)
        {
            return Failed(ReportingDiscrepancyCategory.FeeMismatch, expectedFee, actualFee, billableDeliveryIds.Count);
        }

        return new CompanyReportingReconciliationReadModel(
            true,
            null,
            "Financial",
            campaignId,
            dateRange.FromDateEgypt,
            dateRange.ToDateEgypt,
            expectedCharge,
            actualCharge,
            billableDeliveryIds.Count);

        CompanyReportingReconciliationReadModel Failed(
            ReportingDiscrepancyCategory category,
            decimal expected,
            decimal actual,
            int affectedDeliveryCount)
            => new(
                false,
                category,
                "Financial",
                campaignId,
                dateRange.FromDateEgypt,
                dateRange.ToDateEgypt,
                expected,
                actual,
                affectedDeliveryCount);
    }

    private static bool IsBillable(CompanyReportingDeliverySourceReadModel delivery)
        => delivery.Status is DeliveryStatus.Accepted or DeliveryStatus.Rejected;
}
