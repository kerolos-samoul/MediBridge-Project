using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Interfaces;

public sealed record ViolationSummaryQuery(
    string? DoctorId,
    DoctorMarketplaceStatus? Status,
    string? Eligibility,
    DateOnly? WeekFrom,
    DateOnly? WeekTo,
    int? MinRollingViolations,
    int PageNumber,
    int PageSize);

public interface IAdminActivityEnforcementService
{
    Task<ViolationSummaryPageDto> ListViolationsAsync(ViolationSummaryQuery query, string adminUserId, CancellationToken cancellationToken);
    Task<DoctorEnforcementActionResultDto> ApplyDoctorEnforcementActionAsync(string doctorId, DoctorEnforcementActionRequestDto request, string adminUserId, string? correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityJobRunDto>> ListJobStatusAsync(int take, string adminUserId, CancellationToken cancellationToken);
    Task<ActivityJobRunDto> RunDailyScoreAsync(RunDailyActivityScoreRequestDto request, string adminUserId, CancellationToken cancellationToken);
    Task<ActivityJobRunDto> RunWeeklyEnforcementAsync(RunWeeklyEnforcementRequestDto request, string adminUserId, CancellationToken cancellationToken);
    Task<ActivityJobRunDto> RunSuspensionExpiryAsync(string adminUserId, CancellationToken cancellationToken);
}
