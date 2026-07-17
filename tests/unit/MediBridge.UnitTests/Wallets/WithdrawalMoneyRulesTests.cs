using MediBridge.Core.Interfaces.Wallets;
using Xunit;

namespace MediBridge.UnitTests.Wallets;

public sealed class WithdrawalMoneyRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.234)]
    public void EnsureRequestAmount_RejectsInvalidAmounts(decimal amount)
    {
        Assert.ThrowsAny<Exception>(() => WithdrawalMoneyRules.EnsureRequestAmount(amount, "EGP"));
    }

    [Fact]
    public void EnsureRequestAmount_RejectsNonEgpCurrency()
    {
        Assert.Throws<ArgumentException>(() => WithdrawalMoneyRules.EnsureRequestAmount(10m, "USD"));
    }

    [Fact]
    public void HasSufficientWithdrawableEarnings_ExcludesHoldsPaidAndInconsistentEvidence()
    {
        var balance = new WithdrawalBalanceReadModel(
            "doctor-1",
            SettledDoctorEarnings: 100m,
            PendingWithdrawalHolds: 20m,
            PaidWithdrawals: 10m,
            DisputedOrInconsistentEvidence: 5m,
            OpenRequestAmounts: 15m);

        Assert.Equal(50m, balance.WithdrawableAmount);
        Assert.True(WithdrawalMoneyRules.HasSufficientWithdrawableEarnings(50m, balance));
        Assert.False(WithdrawalMoneyRules.HasSufficientWithdrawableEarnings(50.01m, balance));
    }
}
