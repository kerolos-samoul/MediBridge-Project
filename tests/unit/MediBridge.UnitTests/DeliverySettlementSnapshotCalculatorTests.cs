using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class DeliverySettlementSnapshotCalculatorTests
{
    private readonly DeliverySettlementSnapshotCalculator calculator = new();

    [Theory]
    [InlineData(50, 20, 10, 40)]
    [InlineData(10.05, 10, 1.01, 9.04)]
    public void Calculate_UsesAwayFromZeroRoundingAndReturnsBalancedTwoDecimalSnapshot(
        decimal price,
        decimal percent,
        decimal expectedFee,
        decimal expectedEarnings)
    {
        var result = calculator.Calculate(price, [Policy(percent)]);

        Assert.Equal(price, result.PricePerMessage);
        Assert.Equal(percent, result.PlatformFeePercent);
        Assert.Equal(expectedFee, result.PlatformFeeAmount);
        Assert.Equal(expectedEarnings, result.DoctorEarnings);
        Assert.Equal(price, result.ReservedAmount);
        Assert.Equal(price, result.PlatformFeeAmount + result.DoctorEarnings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Calculate_RejectsOutOfRangePercent(decimal percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(50m, [Policy(percent)]));
    }

    [Fact]
    public void Calculate_AcceptsTheUpperPercentBoundaryOnlyWhenRoundedEarningsRemainPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(50m, [Policy(100m)]));
        Assert.Equal(0.01m, calculator.Calculate(0.02m, [Policy(50m)]).DoctorEarnings);
    }

    [Theory]
    [InlineData(0.01, 1)]
    [InlineData(0.01, 99)]
    public void Calculate_RejectsRoundedZeroFeeOrRoundedZeroEarnings(decimal price, decimal percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(price, [Policy(percent)]));
    }

    [Fact]
    public void Calculate_RejectsMissingAndOverlappingPoliciesWithoutUsingADefault()
    {
        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(50m, []));
        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(50m, [Policy(10m), Policy(20m)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10.001)]
    public void Calculate_RejectsInvalidPrice(decimal price)
    {
        Assert.ThrowsAny<ArgumentException>(() => calculator.Calculate(price, [Policy(20m)]));
    }

    private static EffectivePlatformFeePolicyReadModel Policy(decimal percent) => new("policy", percent);
}
