using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Wallets;

public sealed class WalletTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WalletId { get; set; } = string.Empty;
    public WalletTransactionType OperationType { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? RelatedDeliveryId { get; set; }
    public string? WithdrawalRequestId { get; set; }
    public string? Description { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CorrectsTransactionId { get; set; }
}
