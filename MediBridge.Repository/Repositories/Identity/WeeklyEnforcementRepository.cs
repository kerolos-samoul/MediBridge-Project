using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Profiles;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Identity;

public sealed class WeeklyEnforcementRepository : IWeeklyEnforcementRepository
{
    private readonly MediBridgeDbContext context;

    public WeeklyEnforcementRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public Task<WeeklyEnforcementDecision?> FindDecisionAsync(string doctorId, DateOnly weekStartDateEgypt, CancellationToken cancellationToken = default)
    {
        return context.WeeklyEnforcementDecisions
            .AsNoTracking()
            .SingleOrDefaultAsync(decision => decision.DoctorId == doctorId && decision.WeekStartDateEgypt == weekStartDateEgypt, cancellationToken);
    }

    public async Task AddDecisionAsync(WeeklyEnforcementDecision decision, CancellationToken cancellationToken = default)
    {
        decision.Validate();
        await context.WeeklyEnforcementDecisions.AddAsync(decision, cancellationToken);
    }

    public Task<DoctorWeeklyViolation?> FindViolationAsync(string doctorId, DateOnly weekStartDateEgypt, CancellationToken cancellationToken = default)
    {
        return context.DoctorWeeklyViolations
            .AsNoTracking()
            .SingleOrDefaultAsync(violation => violation.DoctorId == doctorId && violation.WeekStartDateEgypt == weekStartDateEgypt, cancellationToken);
    }

    public async Task AddViolationAsync(DoctorWeeklyViolation violation, CancellationToken cancellationToken = default)
    {
        violation.Validate();
        await context.DoctorWeeklyViolations.AddAsync(violation, cancellationToken);
    }

    public Task<int> CountRollingViolationsAsync(string doctorId, DateOnly fromWeekStartEgypt, DateOnly throughWeekStartEgypt, CancellationToken cancellationToken = default)
    {
        return context.DoctorWeeklyViolations
            .AsNoTracking()
            .CountAsync(violation => violation.DoctorId == doctorId
                && violation.WeekStartDateEgypt >= fromWeekStartEgypt
                && violation.WeekStartDateEgypt <= throughWeekStartEgypt,
                cancellationToken);
    }

    public async Task<IReadOnlyList<DateOnly>> ListRecentViolationWeeksAsync(string doctorId, DateOnly fromWeekStartEgypt, DateOnly throughWeekStartEgypt, CancellationToken cancellationToken = default)
    {
        return await context.DoctorWeeklyViolations
            .AsNoTracking()
            .Where(violation => violation.DoctorId == doctorId
                && violation.WeekStartDateEgypt >= fromWeekStartEgypt
                && violation.WeekStartDateEgypt <= throughWeekStartEgypt)
            .OrderByDescending(violation => violation.WeekStartDateEgypt)
            .Select(violation => violation.WeekStartDateEgypt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ViolationSummaryPageReadModel> ListViolationSummariesAsync(
        ViolationSummaryCriteria criteria,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePageNumber = Math.Max(pageNumber, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var rollingFrom = criteria.WeekFrom ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-56);
        var rollingTo = criteria.WeekTo ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var violationCounts =
            from violation in context.DoctorWeeklyViolations.AsNoTracking()
            where violation.WeekStartDateEgypt >= rollingFrom && violation.WeekStartDateEgypt <= rollingTo
            group violation by violation.DoctorId into grouped
            select new
            {
                DoctorId = grouped.Key,
                RollingViolationCount = grouped.Count()
            };

        var query =
            from profile in context.DoctorProfiles.AsNoTracking()
            join user in context.Users.AsNoTracking() on profile.UserId equals user.Id
            join count in violationCounts on profile.Id equals count.DoctorId
            where !profile.IsDeleted
                && !user.IsDeleted
                && user.Role == UserRole.Doctor
                && user.AccountStatus == AccountStatus.Approved
            select new
            {
                profile,
                DoctorDisplayName = user.Email,
                count.RollingViolationCount
            };

        if (string.IsNullOrWhiteSpace(criteria.DoctorId) is false)
        {
            var doctorId = criteria.DoctorId.Trim();
            query = query.Where(item => item.profile.Id == doctorId);
        }

        if (criteria.Status is not null)
        {
            query = query.Where(item => item.profile.Status == criteria.Status);
        }

        if (criteria.MinRollingViolations is not null)
        {
            query = query.Where(item => item.RollingViolationCount >= criteria.MinRollingViolations.Value);
        }

        if (string.Equals(criteria.Eligibility, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(item => item.RollingViolationCount >= 1 && item.RollingViolationCount <= 5);
        }
        else if (string.Equals(criteria.Eligibility, "ActionEligible", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(item => item.RollingViolationCount > 5);
        }
        else if (string.Equals(criteria.Eligibility, "None", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(item => item.RollingViolationCount == 0);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(item => item.RollingViolationCount)
            .ThenBy(item => item.profile.Id)
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .Select(item => new
            {
                item.profile.Id,
                item.DoctorDisplayName,
                item.profile.Status,
                item.profile.DailyMessageLimit,
                item.profile.MinimumWeeklyRequirement,
                item.profile.ActivityScore,
                item.profile.SuspendedUntilUtc,
                item.RollingViolationCount
            })
            .ToListAsync(cancellationToken);

        var doctorIds = rows.Select(row => row.Id).ToArray();
        var recentWeeks = await context.DoctorWeeklyViolations
            .AsNoTracking()
            .Where(violation => doctorIds.Contains(violation.DoctorId)
                && violation.WeekStartDateEgypt >= rollingFrom
                && violation.WeekStartDateEgypt <= rollingTo)
            .OrderByDescending(violation => violation.WeekStartDateEgypt)
            .Select(violation => new { violation.DoctorId, violation.WeekStartDateEgypt })
            .ToListAsync(cancellationToken);

        var latestActions = await context.DoctorEnforcementActions
            .AsNoTracking()
            .Where(action => doctorIds.Contains(action.DoctorId))
            .OrderByDescending(action => action.EffectiveAtUtc)
            .ThenByDescending(action => action.CreatedAtUtc)
            .Select(action => new { action.DoctorId, action.ActionType, action.EffectiveAtUtc, action.Reason })
            .ToListAsync(cancellationToken);

        var latestActionByDoctor = latestActions
            .GroupBy(action => action.DoctorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var action = group.First();
                    return new LastEnforcementActionReadModel(action.ActionType, action.EffectiveAtUtc, action.Reason);
                },
                StringComparer.Ordinal);

        var weeksByDoctor = recentWeeks
            .GroupBy(item => item.DoctorId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<DateOnly>)group.Select(item => item.WeekStartDateEgypt).ToArray(), StringComparer.Ordinal);

        var items = rows
            .Select(row => new ViolationSummaryReadModel(
                row.Id,
                row.DoctorDisplayName,
                row.Status,
                row.DailyMessageLimit,
                row.MinimumWeeklyRequirement,
                row.ActivityScore,
                row.SuspendedUntilUtc,
                row.RollingViolationCount,
                weeksByDoctor.TryGetValue(row.Id, out var weeks) ? weeks : Array.Empty<DateOnly>(),
                latestActionByDoctor.TryGetValue(row.Id, out var latestAction) ? latestAction : null))
            .ToArray();

        return new ViolationSummaryPageReadModel(items, safePageNumber, safePageSize, total);
    }
}
