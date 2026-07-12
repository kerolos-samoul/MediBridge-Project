using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;

namespace MediBridge.Services.Services;

public sealed record ActivityScoreWindow(DateOnly WindowStartDateEgypt, DateOnly WindowEndDateEgypt);

public sealed record ActivityScoreCalculationResult(
    decimal ResponseSpeedScore,
    decimal EngagementScore,
    decimal FeedbackScore,
    decimal FinalScore,
    ActivityScoreCalculationMode CalculationMode);

public sealed class ActivityScoreCalculator
{
    public static ActivityScoreWindow DeriveWindow(DateOnly scoreDateEgypt)
    {
        return new ActivityScoreWindow(scoreDateEgypt.AddDays(-30), scoreDateEgypt.AddDays(-1));
    }

    public static ActivityScoreCalculationResult Calculate(ActivityScoreAggregateReadModel aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        if (aggregate.DeliveredCount < 0
            || aggregate.InteractedCount < 0
            || aggregate.FeedbackQualifiedCount < 0
            || aggregate.ResponseSpeedContributionCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aggregate), "Activity score aggregate counts cannot be negative.");
        }

        if (aggregate.DeliveredCount == 0)
        {
            return new ActivityScoreCalculationResult(0.0m, 0.0m, 0.0m, 95.0m, ActivityScoreCalculationMode.DefaultNoDeliveries);
        }

        if (aggregate.InteractedCount == 0)
        {
            return new ActivityScoreCalculationResult(0.0m, 0.0m, 0.0m, 0.0m, ActivityScoreCalculationMode.ZeroInteractions);
        }

        var rawResponseSpeed = aggregate.ResponseSpeedContributionCount == 0
            ? 0.0m
            : aggregate.ResponseSpeedContributionSum / aggregate.ResponseSpeedContributionCount;
        rawResponseSpeed = ClampScore(rawResponseSpeed);
        var rawEngagement = ClampScore((decimal)aggregate.InteractedCount / aggregate.DeliveredCount * 100m);
        var rawFeedback = ClampScore((decimal)aggregate.FeedbackQualifiedCount / aggregate.InteractedCount * 100m);
        var final = RoundScore(0.4m * rawResponseSpeed + 0.3m * rawEngagement + 0.3m * rawFeedback);

        return new ActivityScoreCalculationResult(
            RoundScore(rawResponseSpeed),
            RoundScore(rawEngagement),
            RoundScore(rawFeedback),
            ClampScore(final),
            ActivityScoreCalculationMode.Calculated);
    }

    private static decimal RoundScore(decimal value)
    {
        return Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    private static decimal ClampScore(decimal value)
    {
        return Math.Clamp(value, 0.0m, 100.0m);
    }
}
