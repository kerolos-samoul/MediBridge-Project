using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Wallets;

public sealed class WithdrawalRequest : IConcurrencyTrackedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public WithdrawalRequestStatus Status { get; set; } = WithdrawalRequestStatus.Requested;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReviewedByAdminUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public string? PayoutReference { get; set; }
    public byte[] ConcurrencyToken { get; set; } = Array.Empty<byte>();
}
