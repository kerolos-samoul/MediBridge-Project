using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Identity;

public sealed class ContactVerificationFlow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public ContactVerificationChannel Channel { get; set; }
    public string DestinationHash { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int FailedAttemptCount { get; set; }
    public string HashVersion { get; set; } = "hmac-sha256-v1";
    public DateTime? LastSentAtUtc { get; set; }
    public int MaxAttemptCount { get; set; } = 5;
    public int ResendCount { get; set; }
    public DateTime? ResendWindowStartedAtUtc { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public DateTime? MaxAttemptsReachedAtUtc { get; set; }
}
