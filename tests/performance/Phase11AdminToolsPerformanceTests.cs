namespace MediBridge.Performance;

public static class Phase11AdminToolsPerformanceTests
{
    public const int BoundedQueryCountThreshold = 15;
    public static readonly TimeSpan LatencyThreshold = TimeSpan.FromSeconds(2);

    public static Phase11AdminToolsPerformanceResult Evaluate(
        IReadOnlyList<TimeSpan> workQueueDurations,
        IReadOnlyList<TimeSpan> withdrawalDurations,
        IReadOnlyList<TimeSpan> statisticsDurations,
        bool stableWorkQueuePagination,
        bool stableWithdrawalPagination,
        bool noFinancialInconsistencyFalsePositive,
        int? workQueueQueryCount = null,
        int? withdrawalQueryCount = null,
        int? statisticsQueryCount = null)
    {
        return new Phase11AdminToolsPerformanceResult(
            P95(workQueueDurations),
            P95(withdrawalDurations),
            P95(statisticsDurations),
            stableWorkQueuePagination,
            stableWithdrawalPagination,
            noFinancialInconsistencyFalsePositive,
            workQueueQueryCount,
            withdrawalQueryCount,
            statisticsQueryCount);
    }

    public static bool Passed(Phase11AdminToolsPerformanceResult result)
        => WorkQueuePassed(result)
            && WithdrawalsPassed(result)
            && StatisticsPassed(result);

    public static bool WorkQueuePassed(Phase11AdminToolsPerformanceResult result)
        => result.WorkQueueP95 <= LatencyThreshold
            && result.StableWorkQueuePagination
            && QueryCountPassed(result.WorkQueueQueryCount);

    public static bool WithdrawalsPassed(Phase11AdminToolsPerformanceResult result)
        => result.WithdrawalsP95 <= LatencyThreshold
            && result.StableWithdrawalPagination
            && QueryCountPassed(result.WithdrawalQueryCount);

    public static bool StatisticsPassed(Phase11AdminToolsPerformanceResult result)
        => result.StatisticsP95 <= LatencyThreshold
            && result.NoFinancialInconsistencyFalsePositive
            && QueryCountPassed(result.StatisticsQueryCount);

    private static bool QueryCountPassed(int? queryCount)
        => queryCount is null or <= BoundedQueryCountThreshold;

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

public sealed record Phase11AdminToolsPerformanceResult(
    TimeSpan WorkQueueP95,
    TimeSpan WithdrawalsP95,
    TimeSpan StatisticsP95,
    bool StableWorkQueuePagination,
    bool StableWithdrawalPagination,
    bool NoFinancialInconsistencyFalsePositive,
    int? WorkQueueQueryCount,
    int? WithdrawalQueryCount,
    int? StatisticsQueryCount);
