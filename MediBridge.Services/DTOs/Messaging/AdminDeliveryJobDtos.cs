using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Messaging;

public sealed record RunDeliveryJobRequestDto(string? Reason);

public sealed record DeliveryJobEnqueueDto(
    string JobType,
    string SchedulerJobId,
    DateOnly BusinessDateEgypt,
    DateTime EnqueuedAtUtc);

public sealed record DeliveryJobRunSummaryDto(
    string Id,
    DeliveryJobType JobType,
    DateOnly BusinessDateEgypt,
    DeliveryJobRunStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    int ExaminedCount,
    int ActivatedCount,
    int ExpiredCount,
    int CancelledCount,
    int SkippedCount,
    int FailedCount,
    string? SafeFailureSummary);

public sealed record DeliveryRecoveryDispatchSummaryDto(
    string Id,
    DateOnly BusinessDateEgypt,
    DeliveryJobType JobType,
    RecoveryDispatchStatus Status,
    string? SchedulerJobId,
    string? DependsOnDispatchId,
    DateTime ClaimedAtUtc,
    DateTime? EnqueuedAtUtc,
    DateTime? CompletedAtUtc,
    string? SafeFailureSummary);

public sealed record DeliveryJobStatusDto(
    DateOnly BusinessDateEgypt,
    IReadOnlyList<DeliveryJobRunSummaryDto> RecentRuns,
    IReadOnlyList<DeliveryRecoveryDispatchSummaryDto> RecentDispatches);
