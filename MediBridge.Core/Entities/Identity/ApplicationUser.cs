using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Identity;

public sealed class ApplicationUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public UserRole Role { get; set; }
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Pending;
    public bool EmailVerified { get; set; }
    public bool PhoneVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? LastStatusChangedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
