namespace MediBridge.UnitTests;

internal static class Phase9WeeklyEnforcementTestData
{
    public static readonly DateOnly WeekStartDateEgypt = new(2026, 7, 6);
    public static readonly DateOnly WeekEndDateEgypt = new(2026, 7, 13);

    public static WeeklyEnforcementScenario CompliantDoctor() => new(
        WeekStartDateEgypt,
        WeekEndDateEgypt,
        MinimumWeeklyRequirement: 5,
        AcceptCount: 3,
        RejectCount: 2,
        SuspensionOverlap: null,
        ExpectedIsViolation: false,
        ExpectedIsSuspensionSkipped: false);

    public static WeeklyEnforcementScenario BelowThresholdDoctor() => new(
        WeekStartDateEgypt,
        WeekEndDateEgypt,
        MinimumWeeklyRequirement: 5,
        AcceptCount: 2,
        RejectCount: 1,
        SuspensionOverlap: null,
        ExpectedIsViolation: true,
        ExpectedIsSuspensionSkipped: false);

    public static WeeklyEnforcementScenario SuspendedOverlapDoctor() => new(
        WeekStartDateEgypt,
        WeekEndDateEgypt,
        MinimumWeeklyRequirement: 5,
        AcceptCount: 0,
        RejectCount: 0,
        SuspensionOverlap: new SuspensionOverlapRange(
            new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc)),
        ExpectedIsViolation: false,
        ExpectedIsSuspensionSkipped: true);

    public static IReadOnlyList<DateOnly> RollingEightWeekViolations() =>
    [
        new(2026, 5, 18),
        new(2026, 5, 25),
        new(2026, 6, 1),
        new(2026, 6, 8),
        new(2026, 6, 15),
        new(2026, 6, 22),
        new(2026, 6, 29),
        new(2026, 7, 6)
    ];

    public static WarningEligibility WarningStage() => new(RollingViolationCount: 5, IsWarningStage: true, IsActionEligible: false);

    public static WarningEligibility ActionEligible() => new(RollingViolationCount: 6, IsWarningStage: false, IsActionEligible: true);
}

internal sealed record WeeklyEnforcementScenario(
    DateOnly WeekStartDateEgypt,
    DateOnly WeekEndDateEgypt,
    int MinimumWeeklyRequirement,
    int AcceptCount,
    int RejectCount,
    SuspensionOverlapRange? SuspensionOverlap,
    bool ExpectedIsViolation,
    bool ExpectedIsSuspensionSkipped)
{
    public int InteractionCount => AcceptCount + RejectCount;
}

internal sealed record SuspensionOverlapRange(DateTime SuspendedAtUtc, DateTime SuspendedUntilUtc);

internal sealed record WarningEligibility(int RollingViolationCount, bool IsWarningStage, bool IsActionEligible);
