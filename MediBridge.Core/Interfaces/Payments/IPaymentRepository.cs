using MediBridge.Core.Entities.Payments;

namespace MediBridge.Core.Interfaces.Payments;

public interface IPaymentRepository
{
    Task AddMockPaymentAsync(MockPaymentTransaction payment, CancellationToken cancellationToken = default);
    Task<MockPaymentTransaction?> FindByPaymentIdAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<MockPaymentTransaction?> FindByCompanyIdAndIdempotencyKeyAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<MockPaymentTransaction?> FindByCompanyIdAndIdempotencyKeyForUpdateAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<MockPaymentTransaction?> FindByTransactionReferenceAsync(string transactionReference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListPaymentIdsByCompanyAsync(string companyId, CancellationToken cancellationToken = default);
}
