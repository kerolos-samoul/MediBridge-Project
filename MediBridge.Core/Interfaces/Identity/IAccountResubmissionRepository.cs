using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Interfaces.Identity;

public interface IAccountResubmissionRepository
{
    Task AddAsync(AccountResubmission resubmission, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountResubmission>> ListByUserIdAsync(string userId, CancellationToken cancellationToken = default);
}
