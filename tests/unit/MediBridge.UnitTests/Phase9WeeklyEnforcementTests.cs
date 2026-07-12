using FluentAssertions;
using MediBridge.Core.Enums;
using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase9WeeklyEnforcementTests
{
    [Fact]
    public void DeriveLastCompletedWeek_UsesPreviousMondayToMondayWindow()
    {
        var window = WeeklyEnforcementPolicy.DeriveLastCompletedWeek(new DateOnly(2026, 7, 12));

        window.WeekStartDateEgypt.Should().Be(new DateOnly(2026, 6, 29));
        window.WeekEndDateEgypt.Should().Be(new DateOnly(2026, 7, 6));
    }

    [Fact]
    public void DeriveLastCompletedWeek_WhenRunOnMonday_ReturnsPriorCompletedWeek()
    {
        var window = WeeklyEnforcementPolicy.DeriveLastCompletedWeek(new DateOnly(2026, 7, 13));

        window.WeekStartDateEgypt.Should().Be(new DateOnly(2026, 7, 6));
        window.WeekEndDateEgypt.Should().Be(new DateOnly(2026, 7, 13));
    }

    [Theory]
    [InlineData(0, 0, false, WeeklyEnforcementDecisionType.Compliant)]
    [InlineData(5, 5, false, WeeklyEnforcementDecisionType.Compliant)]
    [InlineData(5, 4, false, WeeklyEnforcementDecisionType.Violation)]
    [InlineData(5, 0, true, WeeklyEnforcementDecisionType.SuspensionSkipped)]
    public void ClassifyDecision_UsesMinimumInteractionsAndSuspensionOverlap(
        int minimum,
        int interactions,
        bool suspensionOverlap,
        WeeklyEnforcementDecisionType expected)
    {
        WeeklyEnforcementPolicy.ClassifyDecision(minimum, interactions, suspensionOverlap).Should().Be(expected);
    }

    [Fact]
    public void RollingWindow_StartsAtCurrentWeekMinusSevenWeeks()
    {
        WeeklyEnforcementPolicy.GetRollingWindowStart(new DateOnly(2026, 7, 6))
            .Should().Be(new DateOnly(2026, 5, 18));
    }

    [Theory]
    [InlineData(0, "None")]
    [InlineData(1, "Warning")]
    [InlineData(5, "Warning")]
    [InlineData(6, "ActionEligible")]
    public void DetermineEligibility_ClassifiesRollingCounts(int count, string expected)
    {
        WeeklyEnforcementPolicy.DetermineEligibility(count).Should().Be(expected);
    }
}
