namespace MediBridge.Services.Interfaces;

using MediBridge.Services.DTOs.Wallets;

public interface ICompanyWalletService
{
    Task<CompanyWalletDto> GetCompanyWalletAsync(string actorUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    Task<TopUpCompanyWalletResultDto> TopUpCompanyWalletAsync(string actorUserId, string? idempotencyKey, TopUpCompanyWalletRequestDto request, CancellationToken cancellationToken = default);
}
