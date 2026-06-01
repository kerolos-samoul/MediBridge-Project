namespace MediBridge.Core.Entities.Identity;

public sealed class RefreshCredential
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TokenHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string FamilyId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
