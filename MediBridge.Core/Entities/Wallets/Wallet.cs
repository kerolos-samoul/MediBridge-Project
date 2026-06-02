using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Wallets;

public sealed class Wallet : ISoftDeleteRecord, IConcurrencyTrackedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WalletOwnerType OwnerType { get; set; }
    public string? OwnerUserId { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public decimal AvailableBalance { get; set; }
    public decimal ReservedBalance { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();
}
