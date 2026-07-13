using MediBridge.Core.Enums;

namespace MediBridge.Performance;

public sealed record CompanyReportingPerformanceSeed(
    string CompanyId,
    string CompanyUserId,
    string CampaignId,
    DateOnly FromDateEgypt,
    DateOnly ToDateEgypt,
    int DeliveryCount);

public static class CompanyReportingPerformanceSeedFactory
{
    public const int RequiredDeliveryCount = 10_000;
    public static readonly DateOnly FromDateEgypt = new(2026, 5, 1);
    public static readonly DateOnly ToDateEgypt = new(2026, 7, 29);

    public static IReadOnlyList<CompanyReportingPerformanceDelivery> CreateDeliveries(string campaignId)
    {
        var statuses = new[] { DeliveryStatus.Active, DeliveryStatus.Accepted, DeliveryStatus.Rejected, DeliveryStatus.Expired };
        return Enumerable.Range(0, RequiredDeliveryCount)
            .Select(index => new CompanyReportingPerformanceDelivery(
                $"perf-delivery-{index:00000}",
                campaignId,
                FromDateEgypt.AddDays(index % 90),
                statuses[index % statuses.Length],
                HasFeedback: index % 5 == 0,
                PriceSnapshot: 100m,
                DoctorEarnings: 80m,
                PlatformFee: 20m))
            .ToArray();
    }
}

public sealed record CompanyReportingPerformanceDelivery(
    string DeliveryId,
    string CampaignId,
    DateOnly DeliveryDateEgypt,
    DeliveryStatus Status,
    bool HasFeedback,
    decimal PriceSnapshot,
    decimal DoctorEarnings,
    decimal PlatformFee);
