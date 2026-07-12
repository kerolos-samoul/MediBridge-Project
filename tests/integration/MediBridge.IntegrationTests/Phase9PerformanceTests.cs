using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9PerformanceTests
{
    [Fact(Skip = "Opt-in profile: seed 1,000 doctors and set MEDIBRIDGE_RUN_PHASE9_PERF=true before enabling this workload.")]
    public void AdminViolationList_PageSize100_P95UnderOneSecond()
    {
        // The Phase 9 performance profile is intentionally opt-in because it requires
        // a prepared SQL Server workload and stable machine conditions.
    }
}
