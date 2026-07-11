using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;

namespace MediBridge.UnitTests;

public static class Phase8InteractionTestData
{
    public const string DoctorId = "doctor-phase8";
    public const string ActorUserId = "doctor-user-phase8";
    public const string DeliveryId = "delivery-phase8";
    public const string CampaignId = "campaign-phase8";
    public const string CompanyId = "company-phase8";
    public const string IdempotencyKey = "phase8-idempotency-001";
    public const string PlainFeedback = "Useful clinical reminder.";
    public static readonly DateOnly CairoDate = new(2026, 7, 11);
    public static readonly DateTime DeliveredAtUtc = new(2026, 7, 11, 8, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ReadAtUtc = new(2026, 7, 11, 8, 30, 0, DateTimeKind.Utc);
    public static readonly DateTime InteractedAtUtc = new(2026, 7, 11, 9, 0, 0, DateTimeKind.Utc);

    public static DoctorAdDelivery CreateActiveReservedDelivery() => new()
    {
        Id = DeliveryId,
        DoctorId = DoctorId,
        CampaignId = CampaignId,
        CompanyId = CompanyId,
        DeliveryDateEgypt = CairoDate,
        DeliveredAtUtc = DeliveredAtUtc,
        Status = DeliveryStatus.Active,
        ReservationStatus = ReservationStatus.Reserved,
        PricePerMessageSnapshot = 100m,
        PlatformFeePercentSnapshot = 12.345m,
        PlatformFeeAmount = 12.35m,
        DoctorEarnings = 87.65m,
        ReservedAmount = 100m,
        CreatedAtUtc = DeliveredAtUtc
    };

    public static string Repeat(char character, int count) => new(character, count);
}
