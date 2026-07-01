using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.UnitTests.Wallets;

public sealed class MockPaymentEntityTests
{
    [Fact]
    public void NewPayment_DefaultsToSucceededEgpAndGeneratedReference()
    {
        var payment = new MockPaymentTransaction();

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("EGP", payment.Currency);
        Assert.False(string.IsNullOrWhiteSpace(payment.TransactionReference));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.123)]
    public void Amount_RejectsNonPositiveOrOverPreciseValues(decimal amount)
    {
        var payment = new MockPaymentTransaction();

        Assert.Throws<ArgumentOutOfRangeException>(() => payment.Amount = amount);
    }

    [Fact]
    public void Currency_RejectsNonEgpValues()
    {
        var payment = new MockPaymentTransaction();

        Assert.Throws<ArgumentException>(() => payment.Currency = "USD");
    }

    [Fact]
    public void Status_RejectsAnythingExceptSucceeded()
    {
        var payment = new MockPaymentTransaction();

        Assert.Throws<ArgumentOutOfRangeException>(() => payment.Status = 0);
    }

    [Fact]
    public void BalanceSnapshots_RejectNegativeOrOverPreciseValues()
    {
        var payment = new MockPaymentTransaction();

        Assert.Throws<ArgumentOutOfRangeException>(() => payment.WalletBalanceBefore = -0.01m);
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.WalletBalanceAfter = 10.001m);
    }
}
