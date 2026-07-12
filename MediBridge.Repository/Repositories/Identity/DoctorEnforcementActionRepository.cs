using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Profiles;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Identity;

public sealed class DoctorEnforcementActionRepository : IDoctorEnforcementActionRepository
{
    private readonly MediBridgeDbContext context;

    public DoctorEnforcementActionRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(DoctorEnforcementAction action, CancellationToken cancellationToken = default)
    {
        action.Validate();
        await context.DoctorEnforcementActions.AddAsync(action, cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorEnforcementAction>> ListRecentForDoctorAsync(string doctorId, int take, CancellationToken cancellationToken = default)
    {
        return await context.DoctorEnforcementActions
            .AsNoTracking()
            .Where(action => action.DoctorId == doctorId)
            .OrderByDescending(action => action.EffectiveAtUtc)
            .ThenByDescending(action => action.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, LastEnforcementActionReadModel>> FindLatestByDoctorIdsAsync(
        IReadOnlyCollection<string> doctorIds,
        CancellationToken cancellationToken = default)
    {
        var boundedIds = doctorIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Take(500).ToArray();
        if (boundedIds.Length == 0)
        {
            return new Dictionary<string, LastEnforcementActionReadModel>(StringComparer.Ordinal);
        }

        var rows = await context.DoctorEnforcementActions
            .AsNoTracking()
            .Where(action => boundedIds.Contains(action.DoctorId))
            .OrderByDescending(action => action.EffectiveAtUtc)
            .ThenByDescending(action => action.CreatedAtUtc)
            .Select(action => new { action.DoctorId, action.ActionType, action.EffectiveAtUtc, action.Reason })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.DoctorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var action = group.First();
                    return new LastEnforcementActionReadModel(action.ActionType, action.EffectiveAtUtc, action.Reason);
                },
                StringComparer.Ordinal);
    }

    public Task<bool> AutomaticReactivationExistsAsync(string doctorId, DateTime suspendedUntilUtc, CancellationToken cancellationToken = default)
    {
        if (suspendedUntilUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Suspension expiry must be UTC.", nameof(suspendedUntilUtc));
        }

        return context.DoctorEnforcementActions
            .AsNoTracking()
            .AnyAsync(action => action.DoctorId == doctorId
                && action.ActionType == DoctorEnforcementActionType.AutomaticReactivate
                && action.SuspendedUntilUtc == suspendedUntilUtc,
                cancellationToken);
    }
}
