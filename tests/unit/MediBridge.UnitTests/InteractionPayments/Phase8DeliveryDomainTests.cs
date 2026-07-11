using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.UnitTests.InteractionPayments;

public sealed class Phase8DeliveryDomainTests
{
    [Fact]
    public void MarkRead_FirstWriteWins_AndLeavesSettlementFieldsUnchanged()
    {
        var firstReadAtUtc = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
        var secondReadAtUtc = firstReadAtUtc.AddMinutes(5);
        var delivery = CreateActiveReservedDelivery();

        delivery.MarkRead(firstReadAtUtc);
        delivery.MarkRead(secondReadAtUtc);

        Assert.Equal(firstReadAtUtc, delivery.ReadAtUtc);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.Equal(ReservationStatus.Reserved, delivery.ReservationStatus);
        Assert.Null(delivery.InteractedAtUtc);
        Assert.Null(delivery.FeedbackText);
        Assert.Null(delivery.FeedbackCreatedAtUtc);
        Assert.Equal(50m, delivery.ReservedAmount);
        Assert.Equal(40m, delivery.DoctorEarnings);
    }

    [Fact]
    public void MarkRead_RequiresUtc()
    {
        var delivery = CreateActiveReservedDelivery();
        var localTimestamp = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => delivery.MarkRead(localTimestamp));
        Assert.Null(delivery.ReadAtUtc);
    }

    [Theory]
    [InlineData(DeliveryStatus.Accepted)]
    [InlineData(DeliveryStatus.Rejected)]
    public void MarkInteracted_AllowsAcceptedOrRejected_ChargesReservation_AndLeavesReadUnchanged(DeliveryStatus finalStatus)
    {
        var readAtUtc = new DateTime(2026, 7, 10, 8, 30, 0, DateTimeKind.Utc);
        var interactedAtUtc = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
        var delivery = CreateActiveReservedDelivery(readAtUtc: readAtUtc);

        delivery.MarkInteracted(finalStatus, interactedAtUtc, "useful feedback");

        Assert.Equal(finalStatus, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
        Assert.Equal(interactedAtUtc, delivery.InteractedAtUtc);
        Assert.Equal("useful feedback", delivery.FeedbackText);
        Assert.Equal(interactedAtUtc, delivery.FeedbackCreatedAtUtc);
        Assert.Equal(readAtUtc, delivery.ReadAtUtc);
    }

    [Fact]
    public void MarkInteracted_StoresNoFeedbackTimestamp_WhenFeedbackIsAbsent()
    {
        var interactedAtUtc = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
        var delivery = CreateActiveReservedDelivery();

        delivery.MarkInteracted(DeliveryStatus.Accepted, interactedAtUtc, null);

        Assert.Null(delivery.FeedbackText);
        Assert.Null(delivery.FeedbackCreatedAtUtc);
    }

    [Theory]
    [InlineData(DeliveryStatus.Active)]
    [InlineData(DeliveryStatus.Expired)]
    public void MarkInteracted_RejectsInvalidFinalStatusWithoutPartialMutation(DeliveryStatus invalidStatus)
    {
        var delivery = CreateActiveReservedDelivery();
        var snapshot = Snapshot(delivery);

        Assert.ThrowsAny<ArgumentException>(() =>
            delivery.MarkInteracted(invalidStatus, new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc), null));

        Assert.Equal(snapshot, Snapshot(delivery));
    }

    [Fact]
    public void MarkInteracted_RequiresUtcWithoutPartialMutation()
    {
        var delivery = CreateActiveReservedDelivery();
        var snapshot = Snapshot(delivery);

        Assert.Throws<ArgumentException>(() =>
            delivery.MarkInteracted(DeliveryStatus.Accepted, new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Local), null));

        Assert.Equal(snapshot, Snapshot(delivery));
    }

    [Fact]
    public void MarkInteracted_RejectsRepeatedTransitionWithoutPartialMutation()
    {
        var delivery = CreateActiveReservedDelivery();
        delivery.MarkInteracted(DeliveryStatus.Accepted, new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc), "first");
        var snapshot = Snapshot(delivery);

        Assert.Throws<InvalidOperationException>(() =>
            delivery.MarkInteracted(DeliveryStatus.Rejected, new DateTime(2026, 7, 10, 9, 5, 0, DateTimeKind.Utc), "second"));

        Assert.Equal(snapshot, Snapshot(delivery));
    }

    [Theory]
    [InlineData(DeliveryStatus.Accepted, ReservationStatus.Reserved)]
    [InlineData(DeliveryStatus.Rejected, ReservationStatus.Reserved)]
    [InlineData(DeliveryStatus.Active, ReservationStatus.Charged)]
    [InlineData(DeliveryStatus.Active, ReservationStatus.Released)]
    public void MarkInteracted_RejectsInvalidSourceStateWithoutPartialMutation(DeliveryStatus status, ReservationStatus reservationStatus)
    {
        var delivery = CreateActiveReservedDelivery(status: status, reservationStatus: reservationStatus);
        var snapshot = Snapshot(delivery);

        Assert.Throws<InvalidOperationException>(() =>
            delivery.MarkInteracted(DeliveryStatus.Accepted, new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc), null));

        Assert.Equal(snapshot, Snapshot(delivery));
    }

    private static DoctorAdDelivery CreateActiveReservedDelivery(
        string deliveryId = "phase8-delivery",
        DateTime? readAtUtc = null,
        DeliveryStatus status = DeliveryStatus.Active,
        ReservationStatus reservationStatus = ReservationStatus.Reserved)
    {
        return new DoctorAdDelivery
        {
            Id = deliveryId,
            DoctorId = "phase8-doctor",
            CampaignId = "phase8-campaign",
            CompanyId = "phase8-company",
            DeliveryDateEgypt = new DateOnly(2026, 7, 10),
            DeliveredAtUtc = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
            ReadAtUtc = readAtUtc,
            Status = status,
            ReservationStatus = reservationStatus,
            PricePerMessageSnapshot = 50m,
            PlatformFeePercentSnapshot = 20m,
            PlatformFeeAmount = 10m,
            DoctorEarnings = 40m,
            ReservedAmount = 50m,
            CreatedAtUtc = new DateTime(2026, 7, 10, 7, 55, 0, DateTimeKind.Utc)
        };
    }

    private static DeliverySnapshot Snapshot(DoctorAdDelivery delivery) => new(
        delivery.Status,
        delivery.ReservationStatus,
        delivery.ReadAtUtc,
        delivery.InteractedAtUtc,
        delivery.FeedbackText,
        delivery.FeedbackCreatedAtUtc,
        delivery.UpdatedAtUtc);

    private sealed record DeliverySnapshot(
        DeliveryStatus Status,
        ReservationStatus ReservationStatus,
        DateTime? ReadAtUtc,
        DateTime? InteractedAtUtc,
        string? FeedbackText,
        DateTime? FeedbackCreatedAtUtc,
        DateTime? UpdatedAtUtc);
}
