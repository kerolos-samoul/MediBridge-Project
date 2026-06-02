using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
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

    public Task<string?> FindTransactionIdByIdempotencyAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency is scoped by operation type so different financial operations can reuse external keys safely.
        return context.WalletTransactions
            .Where(transaction => transaction.OperationType == operationType && transaction.IdempotencyKey == idempotencyKey)
            .Select(transaction => transaction.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IdempotencyKeyExistsAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Match the database uniqueness constraint used to reject duplicate retried money-moving operations.
        return context.WalletTransactions.AnyAsync(transaction => transaction.OperationType == operationType && transaction.IdempotencyKey == idempotencyKey, cancellationToken);
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
}
