using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CompanyReportingReconciliationTests
{
    private static readonly CompanyReportingDateRange DateRange = new(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 13));

    [Fact]
    public void ClassifySummaryCountReconciliation_ReturnsCountMismatchWhenStateCountsDoNotMatchDeliveredCount()
    {
        var summary = new CampaignReportSummaryReadModel(
            "campaign-1",
            "Campaign",
            CampaignStatus.Active,
            null,
            null,
            10,
            DeliveredCount: 4,
            ActiveUnansweredCount: 1,
            AcceptedCount: 1,
            RejectedCount: 1,
            ExpiredCount: 0,
            FeedbackCount: 0,
            ReservedAmount: 100m,
            ChargedSpend: 200m,
            DoctorEarnings: 160m,
            PlatformFee: 40m,
            DateTime.UtcNow);

        var result = CompanyReportingCalculations.ClassifySummaryCountReconciliation(summary, DateRange);

        Assert.False(result.IsConsistent);
        Assert.Equal(ReportingDiscrepancyCategory.CountMismatch, result.Category);
        Assert.Equal(4m, result.ExpectedAmount);
        Assert.Equal(3m, result.ActualAmount);
    }

    [Fact]
    public void ClassifyFinancialReconciliation_ReturnsConsistentWhenEvidenceMatchesBillableSnapshots()
    {
        var deliveries = new[] { Delivery("delivery-1", DeliveryStatus.Accepted) };
        var evidence = new[] { Evidence("delivery-1", charged: 100m, earned: 80m, fee: 20m) };

        var result = CompanyReportingCalculations.ClassifyFinancialReconciliation("campaign-1", DateRange, deliveries, evidence);

        Assert.True(result.IsConsistent);
        Assert.Null(result.Category);
    }

    [Fact]
    public void ClassifyFinancialReconciliation_ReturnsMissingEvidenceWhenBillableDeliveriesHaveNoEvidence()
    {
        var result = CompanyReportingCalculations.ClassifyFinancialReconciliation(
            "campaign-1",
            DateRange,
            [Delivery("delivery-1", DeliveryStatus.Accepted)],
            Array.Empty<CompanyReportingFinancialEvidenceReadModel>());

        Assert.False(result.IsConsistent);
        Assert.Equal(ReportingDiscrepancyCategory.MissingEvidence, result.Category);
    }

    [Theory]
    [InlineData(90, 80, 20, ReportingDiscrepancyCategory.ChargeMismatch)]
    [InlineData(100, 70, 20, ReportingDiscrepancyCategory.EarnMismatch)]
    [InlineData(100, 80, 10, ReportingDiscrepancyCategory.FeeMismatch)]
    public void ClassifyFinancialReconciliation_ClassifiesAmountMismatches(decimal charged, decimal earned, decimal fee, ReportingDiscrepancyCategory expected)
    {
        var result = CompanyReportingCalculations.ClassifyFinancialReconciliation(
            "campaign-1",
            DateRange,
            [Delivery("delivery-1", DeliveryStatus.Rejected)],
            [Evidence("delivery-1", charged, earned, fee)]);

        Assert.False(result.IsConsistent);
        Assert.Equal(expected, result.Category);
    }

    [Fact]
    public void ClassifyFinancialReconciliation_DoesNotMutateSourceCollections()
    {
        var deliveries = new[] { Delivery("delivery-1", DeliveryStatus.Accepted) };
        var evidence = new[] { Evidence("delivery-1", charged: 90m, earned: 80m, fee: 20m) };

        _ = CompanyReportingCalculations.ClassifyFinancialReconciliation("campaign-1", DateRange, deliveries, evidence);

        Assert.Equal(DeliveryStatus.Accepted, deliveries[0].Status);
        Assert.Equal(90m, evidence[0].ChargedAmount);
    }

    private static CompanyReportingDeliverySourceReadModel Delivery(string id, DeliveryStatus status)
        => new(
            id,
            "campaign-1",
            status,
            ReservationStatus.Charged,
            new DateOnly(2026, 7, 13),
            100m,
            100m,
            80m,
            20m,
            HasFeedback: false);

    private static CompanyReportingFinancialEvidenceReadModel Evidence(string deliveryId, decimal charged, decimal earned, decimal fee)
        => new(deliveryId, ReservedAmount: 0m, ReleasedAmount: 0m, ChargedAmount: charged, EarnedAmount: earned, PlatformFeeAmount: fee);
}
