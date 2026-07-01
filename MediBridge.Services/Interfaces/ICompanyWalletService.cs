using MediBridge.Services.DTOs.Payments;
using MediBridge.Services.DTOs.Wallets;

namespace MediBridge.Services.Interfaces;

public interface ICompanyWalletService
{
    Task<CompanyWalletDto> GetCompanyWalletAsync(string companyUserId, CancellationToken cancellationToken = default);
    Task<MockPaymentResultDto> CreateMockTopUpAsync(string companyUserId, string idempotencyKey, MockTopUpRequestDto request, CancellationToken cancellationToken = default);
}
