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

    public Task<MockPaymentTransaction?> FindByCompanyAndIdempotencyForUpdateAsync(
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
}
