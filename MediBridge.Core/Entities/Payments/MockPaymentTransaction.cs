using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Payments;

public sealed class MockPaymentTransaction
{
    private decimal amount = 0.01m;
    private string currency = "EGP";
    private PaymentStatus status = PaymentStatus.Succeeded;
    private decimal walletBalanceBefore;
    private decimal walletBalanceAfter;

    public string PaymentId { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public string WalletId { get; set; } = string.Empty;

    public decimal Amount
    {
        get => amount;
        set => amount = MoneyRules.EnsurePositive(value, nameof(Amount));
    }

    public string Currency
    {
        get => currency;
        set
        {
            if (!string.Equals(value, "EGP", StringComparison.Ordinal))
            {
                throw new ArgumentException("Currency must be EGP.", nameof(Currency));
            }

            currency = value;
        }
    }

    public PaymentStatus Status
    {
        get => status;
        set
        {
            if (value != PaymentStatus.Succeeded)
            {
                throw new ArgumentOutOfRangeException(nameof(Status), value, "Mock payments can only be succeeded.");
            }

            status = value;
        }
    }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string TransactionReference { get; set; } = $"MOCK-{Guid.NewGuid():N}";
    public string IdempotencyKey { get; set; } = string.Empty;

    public decimal WalletBalanceBefore
    {
        get => walletBalanceBefore;
        set => walletBalanceBefore = MoneyRules.EnsureValid(value, nameof(WalletBalanceBefore));
    }

    public decimal WalletBalanceAfter
    {
        get => walletBalanceAfter;
        set => walletBalanceAfter = MoneyRules.EnsureValid(value, nameof(WalletBalanceAfter));
    }

    public string WalletTransactionId { get; set; } = string.Empty;
    public string? AuditEventId { get; set; }
}
