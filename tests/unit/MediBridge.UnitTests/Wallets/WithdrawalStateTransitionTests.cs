using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Wallets;
using Xunit;

namespace MediBridge.UnitTests.Wallets;

public sealed class WithdrawalStateTransitionTests
{
    [Theory]
    [InlineData(WithdrawalRequestStatus.Requested, WithdrawalRequestStatus.Approved)]
    [InlineData(WithdrawalRequestStatus.Approved, WithdrawalRequestStatus.Paid)]
    [InlineData(WithdrawalRequestStatus.Approved, WithdrawalRequestStatus.Failed)]
    [InlineData(WithdrawalRequestStatus.Requested, WithdrawalRequestStatus.Rejected)]
    public void CanTransition_AllowsSupportedTransitions(WithdrawalRequestStatus from, WithdrawalRequestStatus to)
    {
        Assert.True(WithdrawalStateTransitions.CanTransition(from, to));
    }

    [Theory]
    [InlineData(WithdrawalRequestStatus.Requested, WithdrawalRequestStatus.Paid)]
    [InlineData(WithdrawalRequestStatus.Requested, WithdrawalRequestStatus.Failed)]
    [InlineData(WithdrawalRequestStatus.Approved, WithdrawalRequestStatus.Rejected)]
    [InlineData(WithdrawalRequestStatus.Rejected, WithdrawalRequestStatus.Approved)]
    [InlineData(WithdrawalRequestStatus.Paid, WithdrawalRequestStatus.Failed)]
    [InlineData(WithdrawalRequestStatus.Failed, WithdrawalRequestStatus.Paid)]
    public void CanTransition_RejectsIncompatibleTransitions(WithdrawalRequestStatus from, WithdrawalRequestStatus to)
    {
        Assert.False(WithdrawalStateTransitions.CanTransition(from, to));
        Assert.Throws<InvalidOperationException>(() => WithdrawalStateTransitions.EnsureCanTransition(from, to));
    }
}
