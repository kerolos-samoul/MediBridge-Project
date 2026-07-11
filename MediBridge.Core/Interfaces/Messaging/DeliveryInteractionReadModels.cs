using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public sealed record MarkDeliveryReadRepositoryResult(
    string DeliveryId,
    DateTime ReadAtUtc,
    bool Created);

public sealed record DeliveryInteractionReplayEvidenceReadModel(
    string OperationId,
    string DeliveryId,
    DeliveryInteractionDecision Decision,
    string? FeedbackText,
    DeliveryInteractionOperationStatus Status,
    string? ChargeTransactionId,
    string? EarnTransactionId,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record LockedInteractionDeliverySnapshot(
    string DeliveryId,
    string DoctorId,
    string CampaignId,
    string CompanyId,
    DateOnly DeliveryDateEgypt,
    DeliveryStatus Status,
    ReservationStatus ReservationStatus,
    DateTime? ReadAtUtc,
    DateTime? InteractedAtUtc,
    string? FeedbackText,
    decimal PricePerMessageSnapshot,
    decimal PlatformFeeAmount,
    decimal DoctorEarnings,
    decimal ReservedAmount);

public sealed record DeliveryInteractionSettlementResultReadModel(
    string DeliveryId,
    DeliveryStatus Status,
    DateTime InteractedAtUtc,
    string? FeedbackText,
    string? ChargeTransactionId,
    string? EarnTransactionId);
