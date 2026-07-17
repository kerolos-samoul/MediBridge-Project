using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Wallets;

public sealed record WithdrawalBalanceReadModel(
    string DoctorId,
    decimal SettledDoctorEarnings,
    decimal PendingWithdrawalHolds,
    decimal PaidWithdrawals,
    decimal DisputedOrInconsistentEvidence,
    decimal OpenRequestAmounts)
{
    public decimal WithdrawableAmount => Math.Max(
        0m,
        SettledDoctorEarnings - PendingWithdrawalHolds - PaidWithdrawals - DisputedOrInconsistentEvidence - OpenRequestAmounts);
}

public sealed record WithdrawalLedgerEvidenceReadModel(
    string WalletTransactionId,
    string WalletLedgerEntryId,
    string WithdrawalRequestId,
    WalletLedgerEntryDirection Direction,
    WalletBalanceType BalanceType,
    decimal Amount,
    WalletTransactionType OperationType,
    DateTime CreatedAtUtc);

public sealed record WithdrawalTransitionRule(
    WithdrawalRequestStatus From,
    WithdrawalRequestStatus To,
    WalletTransactionType? WalletOperation);

public static class WithdrawalStateTransitions
{
    private static readonly IReadOnlyDictionary<WithdrawalRequestStatus, WithdrawalRequestStatus[]> AllowedTransitions =
        new Dictionary<WithdrawalRequestStatus, WithdrawalRequestStatus[]>
        {
            [WithdrawalRequestStatus.Requested] = [WithdrawalRequestStatus.Approved, WithdrawalRequestStatus.Rejected],
            [WithdrawalRequestStatus.Approved] = [WithdrawalRequestStatus.Paid, WithdrawalRequestStatus.Failed],
            [WithdrawalRequestStatus.Rejected] = [],
            [WithdrawalRequestStatus.Paid] = [],
            [WithdrawalRequestStatus.Failed] = []
        };

    public static bool CanTransition(WithdrawalRequestStatus current, WithdrawalRequestStatus requested)
        => AllowedTransitions.TryGetValue(current, out var nextStates) && nextStates.Contains(requested);

    public static void EnsureCanTransition(WithdrawalRequestStatus current, WithdrawalRequestStatus requested)
    {
        if (!CanTransition(current, requested))
        {
            throw new InvalidOperationException($"Withdrawal cannot transition from {current} to {requested}.");
        }
    }
}

public static class WithdrawalMoneyRules
{
    public const string Currency = "EGP";

    public static decimal EnsureRequestAmount(decimal amount, string currency)
    {
        if (!string.Equals(currency, Currency, StringComparison.Ordinal))
        {
            throw new ArgumentException("Withdrawal currency must be EGP.", nameof(currency));
        }

        return MoneyRules.EnsurePositive(amount, nameof(amount));
    }

    public static bool HasSufficientWithdrawableEarnings(decimal requestedAmount, WithdrawalBalanceReadModel balance)
    {
        MoneyRules.EnsurePositive(requestedAmount, nameof(requestedAmount));
        return requestedAmount <= balance.WithdrawableAmount;
    }
}
