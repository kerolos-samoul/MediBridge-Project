namespace MediBridge.Services.DTOs.Campaigns;

public sealed record ReviewDecisionRequestDto(string Decision, string? Reason, string? Notes = null);
