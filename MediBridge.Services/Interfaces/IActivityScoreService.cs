using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Interfaces;

public interface IActivityScoreService
{
    Task<ActivityJobRunDto> RunDailyScoreAsync(DateOnly? scoreDateEgypt, string? requestedByAdminUserId, CancellationToken cancellationToken);
    Task<ActivityJobRunDto> ExpireSuspensionsAsync(string? requestedByAdminUserId, CancellationToken cancellationToken);
}
