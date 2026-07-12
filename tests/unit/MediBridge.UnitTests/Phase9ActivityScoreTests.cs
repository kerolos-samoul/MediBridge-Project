using FluentAssertions;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase9ActivityScoreTests
{
    [Fact]
    public void DeriveWindow_ExcludesScoreDateAndCoversThirtyCompletedCairoDays()
    {
        var window = ActivityScoreCalculator.DeriveWindow(new DateOnly(2026, 7, 12));

        window.WindowStartDateEgypt.Should().Be(new DateOnly(2026, 6, 12));
        window.WindowEndDateEgypt.Should().Be(new DateOnly(2026, 7, 11));
        (window.WindowEndDateEgypt.DayNumber - window.WindowStartDateEgypt.DayNumber).Should().Be(29);
    }

    [Fact]
    public void DeriveWindow_UsesDateOnlySoHostLocalTimeAndDstOffsetDoNotChangeBoundaries()
    {
        var springForwardWindow = ActivityScoreCalculator.DeriveWindow(new DateOnly(2026, 4, 25));
        var autumnWindow = ActivityScoreCalculator.DeriveWindow(new DateOnly(2026, 10, 30));

        springForwardWindow.WindowStartDateEgypt.Should().Be(new DateOnly(2026, 3, 26));
        springForwardWindow.WindowEndDateEgypt.Should().Be(new DateOnly(2026, 4, 24));
        autumnWindow.WindowStartDateEgypt.Should().Be(new DateOnly(2026, 9, 30));
        autumnWindow.WindowEndDateEgypt.Should().Be(new DateOnly(2026, 10, 29));
    }

    [Fact]
    public void Calculate_NoDeliveriesDefaultsToNinetyFive()
    {
        var result = ActivityScoreCalculator.Calculate(new ActivityScoreAggregateReadModel(
            "doctor-1",
            DeliveredCount: 0,
            InteractedCount: 0,
            FeedbackQualifiedCount: 0,
            ResponseSpeedContributionSum: 0m,
            ResponseSpeedContributionCount: 0));

        result.FinalScore.Should().Be(95.0m);
        result.CalculationMode.Should().Be(ActivityScoreCalculationMode.DefaultNoDeliveries);
    }

    [Fact]
    public void Calculate_DeliveriesWithZeroInteractionsProducesZeroScores()
    {
        var result = ActivityScoreCalculator.Calculate(new ActivityScoreAggregateReadModel(
            "doctor-1",
            DeliveredCount: 5,
            InteractedCount: 0,
            FeedbackQualifiedCount: 0,
            ResponseSpeedContributionSum: 0m,
            ResponseSpeedContributionCount: 0));

        result.ResponseSpeedScore.Should().Be(0.0m);
        result.EngagementScore.Should().Be(0.0m);
        result.FeedbackScore.Should().Be(0.0m);
        result.FinalScore.Should().Be(0.0m);
        result.CalculationMode.Should().Be(ActivityScoreCalculationMode.ZeroInteractions);
    }

    [Fact]
    public void Calculate_ComputesWeightedRoundedAndClampedScore()
    {
        var result = ActivityScoreCalculator.Calculate(new ActivityScoreAggregateReadModel(
            "doctor-1",
            DeliveredCount: 4,
            InteractedCount: 3,
            FeedbackQualifiedCount: 2,
            ResponseSpeedContributionSum: 145.8m,
            ResponseSpeedContributionCount: 3));

        result.ResponseSpeedScore.Should().Be(48.6m);
        result.EngagementScore.Should().Be(75.0m);
        result.FeedbackScore.Should().Be(66.7m);
        result.FinalScore.Should().Be(61.9m);
        result.CalculationMode.Should().Be(ActivityScoreCalculationMode.Calculated);
    }

    [Fact]
    public void Calculate_ClampsFinalScoreToBusinessRange()
    {
        var result = ActivityScoreCalculator.Calculate(new ActivityScoreAggregateReadModel(
            "doctor-1",
            DeliveredCount: 1,
            InteractedCount: 1,
            FeedbackQualifiedCount: 1,
            ResponseSpeedContributionSum: 150m,
            ResponseSpeedContributionCount: 1));

        result.ResponseSpeedScore.Should().Be(100.0m);
        result.FinalScore.Should().Be(100.0m);
    }
}
