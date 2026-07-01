using MediBridge.Core.Entities.Payments;

namespace MediBridge.Core.Interfaces.Payments;

public interface IPaymentRepository
{
    Task AddMockPaymentAsync(MockPaymentTransaction payment, CancellationToken cancellationToken = default);
    Task<MockPaymentTransaction?> FindByCompanyAndIdempotencyForUpdateAsync(
        string companyId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
