using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Admin;

public sealed class AdminAccountDecisionRequestDto
{
    public AdminAccountDecisionType Decision { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
