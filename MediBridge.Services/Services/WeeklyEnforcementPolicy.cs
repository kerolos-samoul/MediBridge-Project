using MediBridge.Core.Enums;

namespace MediBridge.Services.Services;

public sealed record WeeklyEnforcementWindow(DateOnly WeekStartDateEgypt, DateOnly WeekEndDateEgypt);

public sealed class WeeklyEnforcementPolicy
{
    public static WeeklyEnforcementWindow DeriveLastCompletedWeek(DateOnly businessDateEgypt)
    {
        var daysSinceMonday = ((int)businessDateEgypt.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var currentWeekStart = businessDateEgypt.AddDays(-daysSinceMonday);
        var completedWeekStart = currentWeekStart.AddDays(-7);
        return new WeeklyEnforcementWindow(completedWeekStart, completedWeekStart.AddDays(7));
    }

    public static WeeklyEnforcementWindow FromWeekStart(DateOnly weekStartDateEgypt)
    {
        ValidateMonday(weekStartDateEgypt);
        return new WeeklyEnforcementWindow(weekStartDateEgypt, weekStartDateEgypt.AddDays(7));
    }

    public static WeeklyEnforcementDecisionType ClassifyDecision(int minimumWeeklyRequirement, int interactionCount, bool suspensionOverlapped)
    {
        if (minimumWeeklyRequirement < 0 || interactionCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumWeeklyRequirement), "Weekly enforcement counts cannot be negative.");
        }

        if (suspensionOverlapped)
        {
            return WeeklyEnforcementDecisionType.SuspensionSkipped;
        }

        return interactionCount >= minimumWeeklyRequirement
            ? WeeklyEnforcementDecisionType.Compliant
            : WeeklyEnforcementDecisionType.Violation;
    }

    public static DateOnly GetRollingWindowStart(DateOnly weekStartDateEgypt)
    {
        ValidateMonday(weekStartDateEgypt);
        return weekStartDateEgypt.AddDays(-49);
    }

    public static string DetermineEligibility(int rollingViolationCount)
    {
        return rollingViolationCount switch
        {
            <= 0 => "None",
            <= 5 => "Warning",
            _ => "ActionEligible"
        };
    }

    private static void ValidateMonday(DateOnly weekStartDateEgypt)
    {
        if (weekStartDateEgypt.DayOfWeek != DayOfWeek.Monday)
        {
            throw new ArgumentException("Week start must be a Monday.", nameof(weekStartDateEgypt));
        }
    }
}
