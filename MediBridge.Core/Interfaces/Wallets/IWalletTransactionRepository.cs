using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Wallets;

public interface IWalletTransactionRepository
{
    Task AddTransactionAsync(string transactionId, string walletId, WalletTransactionType operationType, string idempotencyKey, decimal amount, CancellationToken cancellationToken = default);
    Task AddTransactionAsync(WalletTransaction transaction, CancellationToken cancellationToken = default);
    Task<string?> FindTransactionIdByIdempotencyAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<WalletTransaction?> FindTransactionByIdempotencyAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<WalletTransaction?> FindTransactionByIdAsync(string transactionId, CancellationToken cancellationToken = default);
    Task<bool> IdempotencyKeyExistsAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListWalletTransactionIdsAsync(string walletId, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WalletTransaction>> ListWalletTransactionsAsync(string walletId, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountWalletTransactionsAsync(string walletId, CancellationToken cancellationToken = default);
}
