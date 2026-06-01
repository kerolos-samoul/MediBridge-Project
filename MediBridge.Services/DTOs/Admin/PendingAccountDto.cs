using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.DTOs.Admin;

public sealed class PendingAccountDto
{
    public string UserId { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public AccountStatus AccountStatus { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public VerificationMetadataDto VerificationMetadata { get; set; } = new();
}
