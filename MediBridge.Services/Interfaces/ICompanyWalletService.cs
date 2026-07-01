namespace MediBridge.Services.Interfaces;

using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.DTOs.Payments;

public interface ICompanyWalletService
{
    Task<CompanyWalletDto> GetCompanyWalletAsync(string actorUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    Task<TopUpCompanyWalletResultDto> TopUpCompanyWalletAsync(string actorUserId, string? idempotencyKey, TopUpCompanyWalletRequestDto request, CancellationToken cancellationToken = default);

    Task<MockPaymentResultDto> CreateMockTopUpAsync(string actorUserId, string? idempotencyKey, MockTopUpRequestDto request, CancellationToken cancellationToken = default);
}
