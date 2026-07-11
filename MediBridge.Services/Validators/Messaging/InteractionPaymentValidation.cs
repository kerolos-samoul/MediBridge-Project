using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Messaging;

public static class InteractionPaymentValidation
{
    public static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new Phase8BadRequestException("Idempotency-Key is required.");
        }

        var normalized = idempotencyKey.Trim();
        if (normalized.Length is < 8 or > 128)
        {
            throw new Phase8BadRequestException("Idempotency-Key length must be between 8 and 128 characters.");
        }

        return normalized;
    }

    public static DeliveryInteractionDecision ParseDecision(string? decision)
    {
        var normalized = decision?.Trim();
        return normalized?.ToUpperInvariant() switch
        {
            "ACCEPT" => DeliveryInteractionDecision.Accept,
            "REJECT" => DeliveryInteractionDecision.Reject,
            _ => throw new Phase8BadRequestException("Decision must be Accept or Reject.")
        };
    }

    public static string? NormalizeFeedback(string? feedbackText)
    {
        if (string.IsNullOrWhiteSpace(feedbackText))
        {
            return null;
        }

        var normalized = feedbackText.Trim();
        if (normalized.Length > 1000)
        {
            throw new Phase8BadRequestException("FeedbackText cannot exceed 1,000 characters.");
        }

        return normalized;
    }
}
