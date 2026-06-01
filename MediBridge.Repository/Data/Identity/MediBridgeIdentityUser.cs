using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;
using Microsoft.AspNetCore.Identity;

namespace MediBridge.Repository.Data.Identity;

public sealed class MediBridgeIdentityUser : IdentityUser
{
    public UserRole Role { get; set; }
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Pending;
    public bool EmailVerified { get; set; }
    public bool PhoneVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? LastStatusChangedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public ApplicationUser ToDomain()
    {
        return new ApplicationUser
        {
            Id = Id,
            Email = Email ?? string.Empty,
            PhoneNumber = PhoneNumber,
            Role = Role,
            AccountStatus = AccountStatus,
            EmailVerified = EmailVerified,
            PhoneVerified = PhoneVerified,
            CreatedAtUtc = CreatedAtUtc,
            ApprovedAtUtc = ApprovedAtUtc,
            LastStatusChangedAtUtc = LastStatusChangedAtUtc,
            IsDeleted = IsDeleted,
            DeletedAtUtc = DeletedAtUtc
        };
    }

    public static MediBridgeIdentityUser FromDomain(ApplicationUser user)
    {
        var normalizedEmail = user.Email.Trim().ToUpperInvariant();

        return new MediBridgeIdentityUser
        {
            Id = user.Id,
            UserName = user.Email,
            Email = user.Email,
            NormalizedUserName = normalizedEmail,
            NormalizedEmail = normalizedEmail,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role,
            AccountStatus = user.AccountStatus,
            EmailVerified = user.EmailVerified,
            PhoneVerified = user.PhoneVerified,
            CreatedAtUtc = user.CreatedAtUtc,
            ApprovedAtUtc = user.ApprovedAtUtc,
            LastStatusChangedAtUtc = user.LastStatusChangedAtUtc,
            IsDeleted = user.IsDeleted,
            DeletedAtUtc = user.DeletedAtUtc
        };
    }
}
