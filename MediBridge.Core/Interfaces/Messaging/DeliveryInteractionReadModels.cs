using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public sealed record ReadTrackingReplayReadModel(
    string DeliveryId,
    DeliveryStatus Status,
    DateTime ReadAtUtc,
    bool AlreadyRead);

public sealed record InteractionReplayReadModel(
    string DeliveryId,
    DeliveryStatus Status,
    ReservationStatus ReservationStatus,
    DateTime InteractedAtUtc,
    DateTime? ReadAtUtc,
    DeliveryInteractionOutcome Outcome,
    string? FeedbackText,
    bool FeedbackQualifiesForScore,
    decimal ChargeAmount,
    decimal DoctorEarnings,
    decimal PlatformFeeAmount,
    string RequestFingerprint,
    string IdempotencyKeyHash);

public enum InteractionConflictClassification
{
    None = 0,
    MatchingReplay = 1,
    IdempotencyKeyConflict = 2,
    SettledDeliveryConflict = 3,
    ReservationOrSnapshotAnomaly = 4
}

public sealed record LockedSettlementDeliveryReadModel(
    string DeliveryId,
    string DoctorId,
    string CampaignId,
    string CompanyId,
    DateOnly DeliveryDateEgypt,
    DeliveryStatus Status,
    ReservationStatus ReservationStatus,
    DateTime? ReadAtUtc,
    decimal PricePerMessageSnapshot,
    decimal PlatformFeePercentSnapshot,
    decimal PlatformFeeAmount,
    decimal DoctorEarnings,
    decimal ReservedAmount);

public sealed record DoctorSafeInteractionResultProjection(
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
