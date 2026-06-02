using MediBridge.Core.Entities.Messaging;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase3DeliverySettlementTests
{
    [Fact]
    public void ApplySettlementSnapshot_RejectsDefaultFinancialValues()
    {
        var delivery = new DoctorAdDelivery();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            delivery.ApplySettlementSnapshot(0m, 0m, 0m, 0m, 0m));
    }

    [Fact]
    public void ApplySettlementSnapshot_RejectsInconsistentSettlementTotals()
    {
        var delivery = new DoctorAdDelivery();

        Assert.Throws<ArgumentException>(() =>
            delivery.ApplySettlementSnapshot(50m, 10m, 5m, 40m, 50m));
    }

    [Fact]
    public void ApplySettlementSnapshot_PersistsValidatedFinancialValues()
    {
        var delivery = new DoctorAdDelivery();

        delivery.ApplySettlementSnapshot(50m, 10m, 5m, 45m, 50m);

        Assert.Equal(50m, delivery.PricePerMessageSnapshot);
        Assert.Equal(10m, delivery.PlatformFeePercentSnapshot);
        Assert.Equal(5m, delivery.PlatformFeeAmount);
        Assert.Equal(45m, delivery.DoctorEarnings);
        Assert.Equal(50m, delivery.ReservedAmount);
    }
}
