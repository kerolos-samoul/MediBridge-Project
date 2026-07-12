using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public sealed record ActivityScoreAggregateReadModel(
    string DoctorId,
    int DeliveredCount,
    int InteractedCount,
    int FeedbackQualifiedCount,
    decimal ResponseSpeedContributionSum,
    int ResponseSpeedContributionCount);

public sealed record WeeklyInteractionCountReadModel(
    string DoctorId,
    DateOnly WeekStartDateEgypt,
    DateOnly WeekEndDateEgypt,
    int InteractionCount);

public sealed record ViolationSummaryReadModel(
    string DoctorId,
    string? DoctorDisplayName,
    DoctorMarketplaceStatus Status,
    int DailyMessageLimit,
    int MinimumWeeklyRequirement,
    decimal ActivityScore,
    DateTime? SuspendedUntilUtc,
    int RollingViolationCount,
    IReadOnlyList<DateOnly> RecentViolationWeeks,
    LastEnforcementActionReadModel? LastEnforcementAction);

public sealed record LastEnforcementActionReadModel(
    DoctorEnforcementActionType ActionType,
    DateTime EffectiveAtUtc,
    string? Reason);

public sealed record ActivityJobRunCounters(
    int ProcessedCount,
    int SkippedCount,
    int CreatedCount,
    int UpdatedCount,
    int FailedCount);

public sealed record ActivityJobStatusReadModel(
    string JobRunId,
    ActivityEnforcementJobType JobType,
    DateOnly? TargetScoreDateEgypt,
    DateOnly? TargetWeekStartDateEgypt,
    ActivityEnforcementJobRunStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    ActivityJobRunCounters Counters,
    string? SafeFailureSummary);

public sealed record DoctorSuspensionOverlapReadModel(
    string DoctorId,
    DateTime? SuspendedAtUtc,
    DateTime? SuspendedUntilUtc,
    bool Overlaps);
