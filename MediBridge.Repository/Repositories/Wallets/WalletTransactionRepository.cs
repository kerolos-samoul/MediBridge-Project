using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Wallets;

public sealed class WalletTransactionRepository : IWalletTransactionRepository
{
    private readonly MediBridgeDbContext context;

    public WalletTransactionRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddTransactionAsync(string transactionId, string walletId, WalletTransactionType operationType, string idempotencyKey, decimal amount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        await context.WalletTransactions.AddAsync(new WalletTransaction
        {
            Id = transactionId,
            WalletId = walletId,
            OperationType = operationType,
            IdempotencyKey = idempotencyKey,
            Amount = MoneyRules.EnsurePositive(amount, nameof(amount))
        }, cancellationToken);
    }

    public async Task AddTransactionAsync(WalletTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.IdempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(transaction));
        }

        transaction.Amount = MoneyRules.EnsurePositive(transaction.Amount, nameof(transaction.Amount));
        await context.WalletTransactions.AddAsync(transaction, cancellationToken);
    }

    public Task<string?> FindTransactionIdByIdempotencyAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency is scoped by operation type so different financial operations can reuse external keys safely.
        return context.WalletTransactions
            .Where(transaction => transaction.OperationType == operationType && transaction.IdempotencyKey == idempotencyKey)
            .Select(transaction => transaction.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<WalletTransaction?> FindTransactionByIdempotencyAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return context.WalletTransactions.FirstOrDefaultAsync(
            transaction => transaction.OperationType == operationType && transaction.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }

    public Task<WalletTransaction?> FindTransactionByIdAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        return context.WalletTransactions.FirstOrDefaultAsync(transaction => transaction.Id == transactionId, cancellationToken);
    }

    public Task<bool> IdempotencyKeyExistsAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Match the database uniqueness constraint used to reject duplicate retried money-moving operations.
        return context.WalletTransactions.AnyAsync(transaction => transaction.OperationType == operationType && transaction.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<IReadOnlyList<WalletTransaction>> ListByWithdrawalRequestAsync(string withdrawalRequestId, CancellationToken cancellationToken = default)
    {
        return await context.WalletTransactions
            .AsNoTracking()
            .Where(transaction => transaction.WithdrawalRequestId == withdrawalRequestId)
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListWalletTransactionIdsAsync(string walletId, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default)
    {
        var query = context.WalletTransactions.Where(transaction => transaction.WalletId == walletId);
        if (createdFromUtc is not null)
        {
            query = query.Where(transaction => transaction.CreatedAtUtc >= createdFromUtc);
        }

        if (createdToUtc is not null)
        {
            query = query.Where(transaction => transaction.CreatedAtUtc <= createdToUtc);
        }

        return await query
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .Select(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WalletTransaction>> ListWalletTransactionsAsync(string walletId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await context.WalletTransactions
            .Where(transaction => transaction.WalletId == walletId)
            .OrderByDescending(transaction => transaction.CreatedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Max(take, 1))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountWalletTransactionsAsync(string walletId, CancellationToken cancellationToken = default)
    {
        return context.WalletTransactions.CountAsync(transaction => transaction.WalletId == walletId, cancellationToken);
    }

    public Task<IReadOnlyList<CompanyReportingFinancialEvidenceReadModel>> ListFinancialEvidenceByDeliveryIdsAsync(
        IReadOnlyCollection<string> deliveryIds,
        CancellationToken cancellationToken = default)
    {
        var scopedDeliveryIds = deliveryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (scopedDeliveryIds.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<CompanyReportingFinancialEvidenceReadModel>>(Array.Empty<CompanyReportingFinancialEvidenceReadModel>());
        }

        return ListFinancialEvidenceByDeliveryIdsCoreAsync(scopedDeliveryIds, cancellationToken);
    }

    private async Task<IReadOnlyList<CompanyReportingFinancialEvidenceReadModel>> ListFinancialEvidenceByDeliveryIdsCoreAsync(
        IReadOnlyCollection<string> deliveryIds,
        CancellationToken cancellationToken)
    {
        return await context.WalletTransactions
            .AsNoTracking()
            .Where(transaction => transaction.RelatedDeliveryId != null
                && deliveryIds.Contains(transaction.RelatedDeliveryId)
                && (transaction.OperationType == WalletTransactionType.Reserve
                    || transaction.OperationType == WalletTransactionType.Release
                    || transaction.OperationType == WalletTransactionType.Charge
                    || transaction.OperationType == WalletTransactionType.Earn))
            .GroupBy(transaction => transaction.RelatedDeliveryId!)
            .Select(group => new CompanyReportingFinancialEvidenceReadModel(
                group.Key,
                group.Where(transaction => transaction.OperationType == WalletTransactionType.Reserve).Sum(transaction => transaction.Amount),
                group.Where(transaction => transaction.OperationType == WalletTransactionType.Release).Sum(transaction => transaction.Amount),
                group.Where(transaction => transaction.OperationType == WalletTransactionType.Charge).Sum(transaction => transaction.Amount),
                group.Where(transaction => transaction.OperationType == WalletTransactionType.Earn).Sum(transaction => transaction.Amount),
                group.Where(transaction => transaction.OperationType == WalletTransactionType.Charge).Sum(transaction => transaction.Amount)
                    - group.Where(transaction => transaction.OperationType == WalletTransactionType.Earn).Sum(transaction => transaction.Amount)))
            .ToListAsync(cancellationToken);
    }
}
