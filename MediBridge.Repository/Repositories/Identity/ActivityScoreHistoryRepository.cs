using MediBridge.Core.Interfaces.Profiles;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using ProfileActivityScoreHistory = MediBridge.Core.Entities.Profiles.ActivityScoreHistory;

namespace MediBridge.Repository.Repositories.Identity;

public sealed class ActivityScoreHistoryRepository : IActivityScoreHistoryRepository
{
    private readonly MediBridgeDbContext context;

    public ActivityScoreHistoryRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public Task<ProfileActivityScoreHistory?> FindByDoctorAndDateAsync(string doctorId, DateOnly scoreDateEgypt, CancellationToken cancellationToken = default)
    {
        return context.DoctorActivityScoreHistories
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.DoctorId == doctorId && snapshot.ScoreDateEgypt == scoreDateEgypt, cancellationToken);
    }

    public async Task AddAsync(ProfileActivityScoreHistory snapshot, CancellationToken cancellationToken = default)
    {
        snapshot.Validate();
        await context.DoctorActivityScoreHistories.AddAsync(snapshot, cancellationToken);
    }

    public async Task<IReadOnlyList<ProfileActivityScoreHistory>> ListForDoctorAsync(string doctorId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await context.DoctorActivityScoreHistories
            .AsNoTracking()
            .Where(snapshot => snapshot.DoctorId == doctorId)
            .OrderByDescending(snapshot => snapshot.ScoreDateEgypt)
            .ThenByDescending(snapshot => snapshot.CreatedAtUtc)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(string doctorId, DateOnly scoreDateEgypt, CancellationToken cancellationToken = default)
    {
        return context.DoctorActivityScoreHistories
            .AsNoTracking()
            .AnyAsync(snapshot => snapshot.DoctorId == doctorId && snapshot.ScoreDateEgypt == scoreDateEgypt, cancellationToken);
    }
}
