using MediBridge.Core.Enums;
using MediBridge.Core.Entities.Wallets;

namespace MediBridge.Core.Entities.Messaging;

public sealed class DoctorAdDelivery : IConcurrencyTrackedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public DateOnly DeliveryDateEgypt { get; set; }
    public DateTime DeliveredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Active;
    public DateTime? InteractedAtUtc { get; set; }
    public string? FeedbackText { get; set; }
    public DateTime? FeedbackCreatedAtUtc { get; set; }
    public FeedbackQualityStatus? FeedbackQualityStatus { get; set; }
    public decimal PricePerMessageSnapshot { get; set; }
    public decimal PlatformFeePercentSnapshot { get; set; }
    public decimal PlatformFeeAmount { get; set; }
    public decimal DoctorEarnings { get; set; }
    public decimal ReservedAmount { get; set; }
    public ReservationStatus ReservationStatus { get; set; } = ReservationStatus.Reserved;
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public void ApplySettlementSnapshot(
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount)
    {
        if (platformFeePercentSnapshot <= 0m || platformFeePercentSnapshot > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(platformFeePercentSnapshot), platformFeePercentSnapshot, "Platform fee percent must be between 0 and 100.");
        }

        PricePerMessageSnapshot = MoneyRules.EnsurePositive(pricePerMessageSnapshot, nameof(pricePerMessageSnapshot));
        PlatformFeePercentSnapshot = platformFeePercentSnapshot;
        PlatformFeeAmount = MoneyRules.EnsurePositive(platformFeeAmount, nameof(platformFeeAmount));
        DoctorEarnings = MoneyRules.EnsurePositive(doctorEarnings, nameof(doctorEarnings));
        ReservedAmount = MoneyRules.EnsurePositive(reservedAmount, nameof(reservedAmount));

        if (PlatformFeeAmount + DoctorEarnings != PricePerMessageSnapshot)
        {
            throw new ArgumentException("Platform fee amount plus doctor earnings must equal the price per message snapshot.", nameof(doctorEarnings));
        }

        if (ReservedAmount != PricePerMessageSnapshot)
        {
            throw new ArgumentException("Reserved amount must equal the price per message snapshot.", nameof(reservedAmount));
        }
    }
}
