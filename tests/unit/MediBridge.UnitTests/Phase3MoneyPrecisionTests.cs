using MediBridge.Core.Entities.Wallets;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase3MoneyPrecisionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1.2)]
    [InlineData(1.23)]
    public void EnsureValid_AcceptsValuesWithTwoOrFewerDecimalPlaces(decimal amount)
    {
        Assert.Equal(amount, MoneyRules.EnsureValid(amount, nameof(amount)));
    }

    [Theory]
    [InlineData(1.234)]
    [InlineData(0.001)]
    [InlineData(100.999)]
    public void EnsureValid_RejectsValuesWithMoreThanTwoDecimalPlaces(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneyRules.EnsureValid(amount, nameof(amount)));
    }

    [Fact]
    public void EnsureValid_RejectsNegativeValuesWhenDisallowed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneyRules.EnsureValid(-0.01m, "amount"));
    }

    [Fact]
    public void EnsureValid_AllowsNegativeValuesWhenExplicitlyAllowed()
    {
        Assert.Equal(-1.25m, MoneyRules.EnsureValid(-1.25m, "amount", allowNegative: true));
    }
}
