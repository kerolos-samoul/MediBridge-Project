using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Interfaces.Identity;

public interface IRefreshCredentialRepository
{
    Task AddAsync(RefreshCredential credential, CancellationToken cancellationToken = default);
    Task<RefreshCredential?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<RefreshCredential?> FindByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshCredential>> FindActiveFamilyCredentialsAsync(string familyId, CancellationToken cancellationToken = default);
    Task RevokeByUserAsync(string userId, string reason, CancellationToken cancellationToken = default);
    Task RevokeFamilyAsync(string familyId, string reason, CancellationToken cancellationToken = default);
}
