using MediBridge.Core.Entities.Profiles;

namespace MediBridge.Core.Interfaces.Profiles;

public interface IActivityScoreHistoryRepository
{
    Task<ActivityScoreHistory?> FindByDoctorAndDateAsync(string doctorId, DateOnly scoreDateEgypt, CancellationToken cancellationToken = default);
    Task AddAsync(ActivityScoreHistory snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityScoreHistory>> ListForDoctorAsync(string doctorId, int skip, int take, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string doctorId, DateOnly scoreDateEgypt, CancellationToken cancellationToken = default);
}
