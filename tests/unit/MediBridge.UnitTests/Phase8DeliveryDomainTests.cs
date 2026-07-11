using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase8DeliveryDomainTests
{
    [Fact]
    public void ChargeAndEarnKeys_AreDeterministic_AndRejectBlankDeliveryIds()
    {
        Assert.Equal("delivery:charge:delivery-1", DeliveryFinancialOperationKeys.ForCharge("delivery-1"));
        Assert.Equal("delivery:earn:delivery-1", DeliveryFinancialOperationKeys.ForEarn("delivery-1"));
        Assert.Throws<ArgumentException>(() => DeliveryFinancialOperationKeys.ForCharge(" "));
        Assert.Throws<ArgumentException>(() => DeliveryFinancialOperationKeys.ForEarn(""));
    }

    [Fact]
    public void MarkRead_SetsFirstUtcRead_ThenPreservesReplay()
    {
        var delivery = Phase8InteractionTestData.CreateActiveReservedDelivery();

        var firstWrite = delivery.MarkRead(Phase8InteractionTestData.ReadAtUtc);
        var replay = delivery.MarkRead(Phase8InteractionTestData.ReadAtUtc.AddMinutes(5));

        Assert.True(firstWrite);
        Assert.False(replay);
        Assert.Equal(Phase8InteractionTestData.ReadAtUtc, delivery.ReadAtUtc);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.Equal(ReservationStatus.Reserved, delivery.ReservationStatus);
    }

    [Fact]
    public void MarkRead_RejectsNonUtcTimestamp_AndInvalidStatus()
    {
        var delivery = Phase8InteractionTestData.CreateActiveReservedDelivery();
        Assert.Throws<ArgumentException>(() => delivery.MarkRead(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local)));

        delivery.MarkExpiredAndReleased(Phase8InteractionTestData.InteractedAtUtc);
        Assert.Throws<InvalidOperationException>(() => delivery.MarkRead(Phase8InteractionTestData.InteractedAtUtc.AddMinutes(1)));
    }

    [Theory]
    [InlineData(DeliveryInteractionOutcome.Accept, DeliveryStatus.Accepted)]
    [InlineData(DeliveryInteractionOutcome.Reject, DeliveryStatus.Rejected)]
    public void MarkInteractedAndCharged_TransitionsActiveReservedOnly(
        DeliveryInteractionOutcome outcome,
        DeliveryStatus expectedStatus)
    {
        var delivery = Phase8InteractionTestData.CreateActiveReservedDelivery();

        delivery.MarkInteractedAndCharged(
            outcome,
            Phase8InteractionTestData.InteractedAtUtc,
            Phase8InteractionTestData.PlainFeedback,
            FeedbackQualityStatus.Accepted);

        Assert.Equal(expectedStatus, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
        Assert.Equal(Phase8InteractionTestData.InteractedAtUtc, delivery.InteractedAtUtc);
        Assert.Equal(Phase8InteractionTestData.PlainFeedback, delivery.FeedbackText);
        Assert.Equal(FeedbackQualityStatus.Accepted, delivery.FeedbackQualityStatus);
    }

    [Fact]
    public void MarkInteractedAndCharged_RejectsRepeatedOrNonUtcTransitionsWithoutMutation()
    {
        var delivery = Phase8InteractionTestData.CreateActiveReservedDelivery();
        Assert.Throws<ArgumentException>(() => delivery.MarkInteractedAndCharged(
            DeliveryInteractionOutcome.Accept,
            DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local),
            null,
            null));

        delivery.MarkInteractedAndCharged(DeliveryInteractionOutcome.Accept, Phase8InteractionTestData.InteractedAtUtc, null, null);
        Assert.Throws<InvalidOperationException>(() => delivery.MarkInteractedAndCharged(
            DeliveryInteractionOutcome.Reject,
            Phase8InteractionTestData.InteractedAtUtc.AddMinutes(1),
            null,
            null));
        Assert.Equal(DeliveryStatus.Accepted, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
    }
}
