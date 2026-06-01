using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Admin;

public sealed class AccountDecisionResultDto
{
    public string UserId { get; set; } = string.Empty;
    public AccountStatus ResultingAccountStatus { get; set; }
    public string? ResubmissionToken { get; set; }
}
