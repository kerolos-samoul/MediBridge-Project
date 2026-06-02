using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Wallets;

public sealed class WalletLedgerEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WalletTransactionId { get; set; } = string.Empty;
    public string WalletId { get; set; } = string.Empty;
    public WalletLedgerEntryDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public WalletBalanceType BalanceType { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? CampaignId { get; set; }
    public string? MessageDeliveryId { get; set; }
    public string? DoctorId { get; set; }
    public string? CompanyId { get; set; }
    public string? WithdrawalRequestId { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
