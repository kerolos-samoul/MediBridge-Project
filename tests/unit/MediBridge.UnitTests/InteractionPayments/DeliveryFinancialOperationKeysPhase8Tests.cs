using MediBridge.Core.Entities.Wallets;
using Xunit;

namespace MediBridge.UnitTests.InteractionPayments;

public sealed class DeliveryFinancialOperationKeysPhase8Tests
{
    [Fact]
    public void ForCharge_ReturnsDeliveryScopedChargeKey()
    {
        Assert.Equal("delivery:charge:delivery-123", DeliveryFinancialOperationKeys.ForCharge("delivery-123"));
    }

    [Fact]
    public void ForEarn_ReturnsDeliveryScopedEarnKey()
    {
        Assert.Equal("delivery:earn:delivery-123", DeliveryFinancialOperationKeys.ForEarn("delivery-123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForCharge_RejectsBlankDeliveryId(string? deliveryId)
    {
        Assert.Throws<ArgumentException>(() => DeliveryFinancialOperationKeys.ForCharge(deliveryId!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForEarn_RejectsBlankDeliveryId(string? deliveryId)
    {
        Assert.Throws<ArgumentException>(() => DeliveryFinancialOperationKeys.ForEarn(deliveryId!));
    }
}
