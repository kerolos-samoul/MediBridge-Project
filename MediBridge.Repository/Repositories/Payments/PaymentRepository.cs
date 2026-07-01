using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Interfaces.Payments;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Payments;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly MediBridgeDbContext context;

    public PaymentRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddMockPaymentAsync(MockPaymentTransaction payment, CancellationToken cancellationToken = default)
    {
        await context.MockPaymentTransactions.AddAsync(payment, cancellationToken);
    }

    public Task<MockPaymentTransaction?> FindByPaymentIdAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        return context.MockPaymentTransactions.FirstOrDefaultAsync(payment => payment.PaymentId == paymentId, cancellationToken);
    }

    public Task<MockPaymentTransaction?> FindByCompanyIdAndIdempotencyKeyAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return context.MockPaymentTransactions
            .FirstOrDefaultAsync(payment => payment.CompanyId == companyId && payment.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public Task<MockPaymentTransaction?> FindByCompanyIdAndIdempotencyKeyForUpdateAsync(
        string companyId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return context.MockPaymentTransactions
            .FromSqlInterpolated($"""
                SELECT *
                FROM [MockPaymentTransactions] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [CompanyId] = {companyId}
                    AND [IdempotencyKey] = {idempotencyKey}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<MockPaymentTransaction?> FindByTransactionReferenceAsync(string transactionReference, CancellationToken cancellationToken = default)
    {
        return context.MockPaymentTransactions.FirstOrDefaultAsync(payment => payment.TransactionReference == transactionReference, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListPaymentIdsByCompanyAsync(string companyId, CancellationToken cancellationToken = default)
    {
        return await context.MockPaymentTransactions
            .Where(payment => payment.CompanyId == companyId)
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .Select(payment => payment.PaymentId)
            .ToListAsync(cancellationToken);
    }
}
