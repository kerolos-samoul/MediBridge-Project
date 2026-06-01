namespace MediBridge.Core.Entities.Identity;

public sealed class AccountResubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public string UpdatedProfileFields { get; set; } = string.Empty;
    public string UpdatedVerificationMetadata { get; set; } = string.Empty;
}
