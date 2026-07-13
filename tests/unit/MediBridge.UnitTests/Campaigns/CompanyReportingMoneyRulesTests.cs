using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CompanyReportingMoneyRulesTests
{
    private static readonly CompanyReportingDateRange DateRange = new(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 13));

    [Fact]
    public void BuildAnalytics_ActiveDeliveriesContributeReservedAmountOnly()
    {
        var analytics = CompanyReportingCalculations.BuildAnalytics("campaign-1", DateRange, [Delivery("active", DeliveryStatus.Active, hasFeedback: false)]);

        Assert.Equal(1, analytics.DeliveredCount);
        Assert.Equal(1, analytics.ActiveUnansweredCount);
        Assert.Equal(100m, analytics.ReservedAmount);
        Assert.Equal(0m, analytics.ChargedSpend);
        Assert.Equal(0m, analytics.DoctorEarnings);
        Assert.Equal(0m, analytics.PlatformFee);
    }

    [Theory]
    [InlineData(DeliveryStatus.Accepted)]
    [InlineData(DeliveryStatus.Rejected)]
    public void BuildAnalytics_AcceptedAndRejectedDeliveriesContributeChargedSpendEarningsAndFee(DeliveryStatus status)
    {
        var analytics = CompanyReportingCalculations.BuildAnalytics("campaign-1", DateRange, [Delivery("billable", status, hasFeedback: true)]);

        Assert.Equal(1, analytics.DeliveredCount);
        Assert.Equal(status == DeliveryStatus.Accepted ? 1 : 0, analytics.AcceptedCount);
        Assert.Equal(status == DeliveryStatus.Rejected ? 1 : 0, analytics.RejectedCount);
        Assert.Equal(0m, analytics.ReservedAmount);
        Assert.Equal(100m, analytics.ChargedSpend);
        Assert.Equal(80m, analytics.DoctorEarnings);
        Assert.Equal(20m, analytics.PlatformFee);
        Assert.Equal(1, analytics.FeedbackCount);
    }

    [Fact]
    public void BuildAnalytics_ExpiredDeliveriesContributeCountsButNoMoney()
    {
        var analytics = CompanyReportingCalculations.BuildAnalytics("campaign-1", DateRange, [Delivery("expired", DeliveryStatus.Expired, hasFeedback: false)]);

        Assert.Equal(1, analytics.DeliveredCount);
        Assert.Equal(1, analytics.ExpiredCount);
        Assert.Equal(0m, analytics.ReservedAmount);
        Assert.Equal(0m, analytics.ChargedSpend);
        Assert.Equal(0m, analytics.DoctorEarnings);
        Assert.Equal(0m, analytics.PlatformFee);
    }

    private static CompanyReportingDeliverySourceReadModel Delivery(string id, DeliveryStatus status, bool hasFeedback)
        => new(
            id,
            "campaign-1",
            status,
            status == DeliveryStatus.Active ? ReservationStatus.Reserved : status == DeliveryStatus.Expired ? ReservationStatus.Released : ReservationStatus.Charged,
            new DateOnly(2026, 7, 13),
            100m,
            100m,
            80m,
            20m,
            hasFeedback);
}
