using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Admin;

public sealed record ViolationSummaryDto(
    string DoctorId,
    string? DoctorDisplayName,
    DoctorMarketplaceStatus Status,
    int DailyMessageLimit,
    int MinimumWeeklyRequirement,
    decimal ActivityScore,
    DateTime? SuspendedUntilUtc,
    int RollingViolationCount,
    string Eligibility,
    IReadOnlyList<DateOnly> RecentViolationWeeks,
    LastEnforcementActionDto? LastEnforcementAction);

public sealed record ViolationSummaryPageDto(
    IReadOnlyList<ViolationSummaryDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record LastEnforcementActionDto(
    DoctorEnforcementActionType ActionType,
    DateTime EffectiveAtUtc,
    string? Reason);

public sealed record DoctorEnforcementActionRequestDto(
    DoctorEnforcementActionType? ActionType,
    string? Reason,
    int? NewDailyMessageLimit,
    DateTime? SuspendedUntilUtc);

public sealed record DoctorEnforcementActionResultDto(
    string DoctorId,
    DoctorMarketplaceStatus Status,
    int DailyMessageLimit,
    DateTime? SuspendedUntilUtc,
    DoctorEnforcementActionType ActionType,
    DateTime EffectiveAtUtc,
    string? AuditEventId);

public sealed record ActivityJobRunDto(
    string JobRunId,
    ActivityEnforcementJobType JobType,
    DateOnly? TargetScoreDateEgypt,
    DateOnly? TargetWeekStartDateEgypt,
    ActivityEnforcementJobRunStatus Status,
    int ProcessedCount,
    int SkippedCount,
    int CreatedCount,
    int UpdatedCount,
    int FailedCount,
    string? SafeFailureSummary);

public sealed record RunDailyActivityScoreRequestDto(DateOnly? ScoreDateEgypt);

public sealed record RunWeeklyEnforcementRequestDto(DateOnly? WeekStartDateEgypt);
