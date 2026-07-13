namespace MediBridge.Performance;

public static class CompanyReportingPerformanceTests
{
    public const int BoundedQueryCountThreshold = 10;
    public static readonly TimeSpan LatencyThreshold = TimeSpan.FromSeconds(2);

    public static CompanyReportingPerformanceResult Evaluate(
        IReadOnlyList<TimeSpan> summaryDurations,
        IReadOnlyList<TimeSpan> deliveryDurations,
        IReadOnlyList<TimeSpan> feedbackDurations,
        IReadOnlyList<TimeSpan> analyticsDurations,
        int falseDiscrepancyCount,
        int? summaryQueryCount = null,
        int? deliveryQueryCount = null,
        int? feedbackQueryCount = null,
        int? analyticsQueryCount = null)
    {
        return new CompanyReportingPerformanceResult(
            P95(summaryDurations),
            P95(deliveryDurations),
            P95(feedbackDurations),
            P95(analyticsDurations),
            falseDiscrepancyCount,
            summaryQueryCount,
            deliveryQueryCount,
            feedbackQueryCount,
            analyticsQueryCount);
    }

    public static bool Passed(CompanyReportingPerformanceResult result)
        => SummaryPassed(result)
            && DeliveryPassed(result)
            && FeedbackPassed(result)
            && AnalyticsPassed(result);

    public static bool SummaryPassed(CompanyReportingPerformanceResult result)
        => result.SummaryP95 <= LatencyThreshold
            && QueryCountPassed(result.SummaryQueryCount);

    public static bool DeliveryPassed(CompanyReportingPerformanceResult result)
        => result.DeliveryP95 <= LatencyThreshold
            && QueryCountPassed(result.DeliveryQueryCount);

    public static bool FeedbackPassed(CompanyReportingPerformanceResult result)
        => result.FeedbackP95 <= LatencyThreshold
            && QueryCountPassed(result.FeedbackQueryCount);

    public static bool AnalyticsPassed(CompanyReportingPerformanceResult result)
        => result.AnalyticsP95 <= LatencyThreshold
            && QueryCountPassed(result.AnalyticsQueryCount)
            && result.FalseDiscrepancyCount == 0;

    private static bool QueryCountPassed(int? queryCount)
    {
        return queryCount is null or <= BoundedQueryCountThreshold;
    }

    private static TimeSpan P95(IReadOnlyList<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var index = (int)Math.Ceiling(ordered.Length * 0.95m) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }
}

public sealed record CompanyReportingPerformanceResult(
    TimeSpan SummaryP95,
    TimeSpan DeliveryP95,
    TimeSpan FeedbackP95,
    TimeSpan AnalyticsP95,
    int FalseDiscrepancyCount,
    int? SummaryQueryCount,
    int? DeliveryQueryCount,
    int? FeedbackQueryCount,
    int? AnalyticsQueryCount);
