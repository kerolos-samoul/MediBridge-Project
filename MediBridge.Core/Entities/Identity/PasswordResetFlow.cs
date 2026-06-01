namespace MediBridge.Core.Entities.Identity;

public sealed class PasswordResetFlow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string RequestCorrelationId { get; set; } = string.Empty;
}
