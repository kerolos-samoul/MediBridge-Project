using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Interfaces;

public interface IAdminAccountService
{
    Task<PendingAccountPageDto> ListPendingAccountsAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<AccountDecisionResultDto> ApplyDecisionAsync(string adminUserId, string targetUserId, AdminAccountDecisionRequestDto request, CancellationToken cancellationToken = default);
}
