using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Interfaces;

public interface IWeeklyEnforcementService
{
    Task<ActivityJobRunDto> RunWeeklyEnforcementAsync(DateOnly? weekStartDateEgypt, string? requestedByAdminUserId, CancellationToken cancellationToken);
}
