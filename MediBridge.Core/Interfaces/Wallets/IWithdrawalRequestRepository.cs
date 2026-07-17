using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Admin;

namespace MediBridge.Core.Interfaces.Wallets;

public interface IWithdrawalRequestRepository
{
    Task AddAsync(WithdrawalRequest request, CancellationToken cancellationToken = default);
    Task<WithdrawalRequest?> FindByIdAsync(string withdrawalRequestId, CancellationToken cancellationToken = default);
    Task<WithdrawalRequest?> FindByIdForUpdateAsync(string withdrawalRequestId, CancellationToken cancellationToken = default);
    Task<WithdrawalRequest?> FindByIdWithConcurrencyTokenAsync(string withdrawalRequestId, byte[] concurrencyToken, CancellationToken cancellationToken = default);
    Task<AdminPageReadModel<WithdrawalRequest>> ListDoctorOwnedAsync(string doctorId, WithdrawalRequestStatus? status, AdminPagination pagination, CancellationToken cancellationToken = default);
    Task<AdminPageReadModel<WithdrawalRequest>> ListAdminAsync(AdminWithdrawalFilters filters, CancellationToken cancellationToken = default);
    Task<int> CountAsync(AdminWithdrawalFilters filters, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<WithdrawalRequestStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default);
}
