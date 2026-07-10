namespace MediBridge.Services.DTOs.Messaging;

public static class InteractionPaymentResultStatus
{
    public const string Created = "Created";
    public const string Replayed = "Replayed";
}

public sealed class MarkReadResultDto
{
    public string DeliveryId { get; set; } = string.Empty;
    public DateTime ReadAtUtc { get; set; }
    public string ReadStatus { get; set; } = InteractionPaymentResultStatus.Created;
}

public sealed class InteractDeliveryRequestDto
{
    public string Decision { get; set; } = string.Empty;
    public string? FeedbackText { get; set; }
}

public sealed class InteractDeliveryResultDto
{
    public string DeliveryId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime InteractedAtUtc { get; set; }
    public string? FeedbackText { get; set; }
    public string IdempotencyStatus { get; set; } = InteractionPaymentResultStatus.Created;
}
