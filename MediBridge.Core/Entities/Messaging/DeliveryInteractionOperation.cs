using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Messaging;

public sealed class DeliveryInteractionOperation : IConcurrencyTrackedRecord
{
    public const int MinIdempotencyKeyLength = 8;
    public const int MaxIdempotencyKeyLength = 128;
    public const int MaxFeedbackTextLength = 1000;
    public const int MaxSafeFailureSummaryLength = 2000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public string DeliveryId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DeliveryInteractionDecision Decision { get; set; }
    public string? FeedbackText { get; set; }
    public DeliveryInteractionOperationStatus Status { get; set; } = DeliveryInteractionOperationStatus.Created;
    public string? ChargeTransactionId { get; set; }
    public string? EarnTransactionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? SafeFailureSummary { get; set; }
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();

    public static DeliveryInteractionOperation Create(
        string doctorId,
        string deliveryId,
        string idempotencyKey,
        DeliveryInteractionDecision decision,
        string? feedbackText,
        DateTime createdAtUtc)
    {
        EnsureRequired(doctorId, nameof(doctorId));
        EnsureRequired(deliveryId, nameof(deliveryId));
        EnsureIdempotencyKey(idempotencyKey);
        EnsureFeedback(feedbackText);
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new DeliveryInteractionOperation
        {
            DoctorId = doctorId,
            DeliveryId = deliveryId,
            IdempotencyKey = idempotencyKey,
            Decision = decision,
            FeedbackText = feedbackText,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void MarkSucceeded(string chargeTransactionId, string earnTransactionId, DateTime completedAtUtc)
    {
        EnsureRequired(chargeTransactionId, nameof(chargeTransactionId));
        EnsureRequired(earnTransactionId, nameof(earnTransactionId));
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));

        Status = DeliveryInteractionOperationStatus.Succeeded;
        ChargeTransactionId = chargeTransactionId;
        EarnTransactionId = earnTransactionId;
        CompletedAtUtc = completedAtUtc;
        SafeFailureSummary = null;
    }

    public void MarkFailed(string safeFailureSummary, DateTime completedAtUtc)
    {
        EnsureSafeFailureSummary(safeFailureSummary);
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));

        Status = DeliveryInteractionOperationStatus.Failed;
        SafeFailureSummary = safeFailureSummary;
        CompletedAtUtc = completedAtUtc;
    }

    private static void EnsureRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A nonblank value is required.", parameterName);
        }
    }

    private static void EnsureIdempotencyKey(string value)
    {
        EnsureRequired(value, nameof(IdempotencyKey));
        if (value.Length is < MinIdempotencyKeyLength or > MaxIdempotencyKeyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(IdempotencyKey), value.Length, "Idempotency key length must be between 8 and 128 characters.");
        }
    }

    private static void EnsureFeedback(string? value)
    {
        if (value is { Length: > MaxFeedbackTextLength })
        {
            throw new ArgumentOutOfRangeException(nameof(FeedbackText), value.Length, "Feedback text cannot exceed 1,000 characters.");
        }
    }

    private static void EnsureSafeFailureSummary(string value)
    {
        EnsureRequired(value, nameof(SafeFailureSummary));
        if (value.Length > MaxSafeFailureSummaryLength)
        {
            throw new ArgumentOutOfRangeException(nameof(SafeFailureSummary), value.Length, "Safe failure summary cannot exceed 2,000 characters.");
        }

        if (value.Contains(Environment.NewLine, StringComparison.Ordinal))
        {
            throw new ArgumentException("Safe failure summary must not contain raw stack traces.", nameof(SafeFailureSummary));
        }
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The operation timestamp must be UTC.", parameterName);
        }
    }
}
