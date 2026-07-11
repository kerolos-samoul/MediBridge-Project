using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Messaging;

public sealed record ReadTrackingResultDto(
    string DeliveryId,
    DeliveryStatus Status,
    DateTime ReadAtUtc,
    bool AlreadyRead);

public sealed record DoctorInteractionRequestDto(
    string Outcome,
    string? Feedback);

public sealed record DoctorInteractionResultDto(
    string DeliveryId,
    DeliveryStatus Status,
    ReservationStatus ReservationStatus,
    DateTime InteractedAtUtc,
    DateTime? ReadAtUtc,
    bool FeedbackAccepted,
    bool FeedbackQualifiesForScore,
    decimal ChargeAmount,
    decimal DoctorEarnings,
    decimal PlatformFeeAmount,
    bool Replayed);
