using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Interfaces.Messaging;

namespace MediBridge.Core.Interfaces.Profiles;

public interface IDoctorEnforcementActionRepository
{
    Task AddAsync(DoctorEnforcementAction action, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorEnforcementAction>> ListRecentForDoctorAsync(string doctorId, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, LastEnforcementActionReadModel>> FindLatestByDoctorIdsAsync(IReadOnlyCollection<string> doctorIds, CancellationToken cancellationToken = default);
    Task<bool> AutomaticReactivationExistsAsync(string doctorId, DateTime suspendedUntilUtc, CancellationToken cancellationToken = default);
}
