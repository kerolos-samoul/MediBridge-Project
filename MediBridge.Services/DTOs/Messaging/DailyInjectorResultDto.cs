using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Messaging;

public sealed record DailyInjectorResultDto(
    DateOnly BusinessDateEgypt,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    DeliveryJobRunStatus Outcome,
    int ExaminedCount,
    int ActivatedCount,
    int CancelledCount,
    int SkippedCount,
    int FailedCount,
    string? FailureSummary);
