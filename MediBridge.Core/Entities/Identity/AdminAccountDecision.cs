using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Identity;

public sealed class AdminAccountDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AdminUserId { get; set; } = string.Empty;
    public ApplicationUser? AdminUser { get; set; }
    public string TargetUserId { get; set; } = string.Empty;
    public ApplicationUser? TargetUser { get; set; }
    public AdminAccountDecisionType Decision { get; set; }
    public AccountStatus ResultingAccountStatus { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
