using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Interfaces.Identity;

public interface IAdminAccountDecisionRepository
{
    Task AddAsync(AdminAccountDecision decision, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminAccountDecision>> ListByTargetUserIdAsync(string targetUserId, CancellationToken cancellationToken = default);
}
