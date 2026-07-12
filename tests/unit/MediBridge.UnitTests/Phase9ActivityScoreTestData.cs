namespace MediBridge.UnitTests;

internal static class Phase9ActivityScoreTestData
{
    public static readonly DateOnly ScoreDateEgypt = new(2026, 7, 12);
    public static readonly DateOnly WindowStartDateEgypt = new(2026, 6, 12);
    public static readonly DateOnly WindowEndDateEgypt = new(2026, 7, 11);

    public const decimal DefaultNoDeliveriesScore = 95.0m;
    public const decimal ZeroInteractionScore = 0.0m;

    public static ActivityScoreScenario NoDeliveries() => new(
        ScoreDateEgypt,
        WindowStartDateEgypt,
        WindowEndDateEgypt,
        DeliveredCount: 0,
        InteractedCount: 0,
        FeedbackQualifiedCount: 0,
        ResponseSamples: Array.Empty<ResponseTimeSample>(),
        ExpectedResponseSpeedScore: 0.0m,
        ExpectedEngagementScore: 0.0m,
        ExpectedFeedbackScore: 0.0m,
        ExpectedFinalScore: DefaultNoDeliveriesScore);

    public static ActivityScoreScenario DeliveredWithoutInteractions() => new(
        ScoreDateEgypt,
        WindowStartDateEgypt,
        WindowEndDateEgypt,
        DeliveredCount: 4,
        InteractedCount: 0,
        FeedbackQualifiedCount: 0,
        ResponseSamples: Array.Empty<ResponseTimeSample>(),
        ExpectedResponseSpeedScore: 0.0m,
        ExpectedEngagementScore: 0.0m,
        ExpectedFeedbackScore: 0.0m,
        ExpectedFinalScore: ZeroInteractionScore);

    public static ActivityScoreScenario MixedInteractions() => new(
        ScoreDateEgypt,
        WindowStartDateEgypt,
        WindowEndDateEgypt,
        DeliveredCount: 4,
        InteractedCount: 3,
        FeedbackQualifiedCount: 2,
        ResponseSamples:
        [
            new ResponseTimeSample(TimeSpan.FromHours(1), 95.8m),
            new ResponseTimeSample(TimeSpan.FromHours(12), 50.0m),
            new ResponseTimeSample(TimeSpan.FromHours(30), 0.0m)
        ],
        ExpectedResponseSpeedScore: 48.6m,
        ExpectedEngagementScore: 75.0m,
        ExpectedFeedbackScore: 66.7m,
        ExpectedFinalScore: 61.9m);

    public static DoctorActivityState ApprovedActiveDoctor() => new(
        DoctorId: "doctor-approved-active",
        IsApproved: true,
        IsDeleted: false,
        IsSuspended: false);

    public static DoctorActivityState ApprovedSuspendedDoctor() => new(
        DoctorId: "doctor-approved-suspended",
        IsApproved: true,
        IsDeleted: false,
        IsSuspended: true);

    public static DoctorActivityState DeletedDoctor() => new(
        DoctorId: "doctor-deleted",
        IsApproved: true,
        IsDeleted: true,
        IsSuspended: false);

    public static DoctorActivityState UnapprovedDoctor() => new(
        DoctorId: "doctor-unapproved",
        IsApproved: false,
        IsDeleted: false,
        IsSuspended: false);
}

internal sealed record ActivityScoreScenario(
    DateOnly ScoreDateEgypt,
    DateOnly WindowStartDateEgypt,
    DateOnly WindowEndDateEgypt,
    int DeliveredCount,
    int InteractedCount,
    int FeedbackQualifiedCount,
    IReadOnlyList<ResponseTimeSample> ResponseSamples,
    decimal ExpectedResponseSpeedScore,
    decimal ExpectedEngagementScore,
    decimal ExpectedFeedbackScore,
    decimal ExpectedFinalScore);

internal sealed record ResponseTimeSample(TimeSpan ResponseTime, decimal ExpectedContribution);

internal sealed record DoctorActivityState(
    string DoctorId,
    bool IsApproved,
    bool IsDeleted,
    bool IsSuspended);
