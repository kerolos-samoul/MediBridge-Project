using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;

namespace MediBridge.Core.Interfaces.Profiles;

public sealed record ViolationSummaryCriteria(
    string? DoctorId,
    DoctorMarketplaceStatus? Status,
    string? Eligibility,
    DateOnly? WeekFrom,
    DateOnly? WeekTo,
    int? MinRollingViolations);

public sealed record ViolationSummaryPageReadModel(
    IReadOnlyList<ViolationSummaryReadModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

public interface IWeeklyEnforcementRepository
{
    Task<WeeklyEnforcementDecision?> FindDecisionAsync(string doctorId, DateOnly weekStartDateEgypt, CancellationToken cancellationToken = default);
    Task AddDecisionAsync(WeeklyEnforcementDecision decision, CancellationToken cancellationToken = default);
    Task<DoctorWeeklyViolation?> FindViolationAsync(string doctorId, DateOnly weekStartDateEgypt, CancellationToken cancellationToken = default);
    Task AddViolationAsync(DoctorWeeklyViolation violation, CancellationToken cancellationToken = default);
    Task<int> CountRollingViolationsAsync(string doctorId, DateOnly fromWeekStartEgypt, DateOnly throughWeekStartEgypt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DateOnly>> ListRecentViolationWeeksAsync(string doctorId, DateOnly fromWeekStartEgypt, DateOnly throughWeekStartEgypt, CancellationToken cancellationToken = default);
    Task<ViolationSummaryPageReadModel> ListViolationSummariesAsync(ViolationSummaryCriteria criteria, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}
