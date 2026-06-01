using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Identity;

public interface IApplicationUserRepository
{
    Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> FindByContactAsync(string contact, CancellationToken cancellationToken = default);
    Task<bool> ValidatePasswordAsync(string email, string password, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default);
    Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default);
    Task AddAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default);
    Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationUser>> ListByStatusAsync(AccountStatus status, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(AccountStatus status, CancellationToken cancellationToken = default);
}
