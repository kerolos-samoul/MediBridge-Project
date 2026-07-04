using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Messaging;

public sealed record DeliveryJobResultDto(
    DateOnly BusinessDateEgypt,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    DeliveryJobRunStatus Outcome,
    int ExaminedCount,
    int ExpiredCount,
    int SkippedCount,
    int FailedCount,
    string? FailureSummary);
