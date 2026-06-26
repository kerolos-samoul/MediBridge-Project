using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Wallets;

public interface IWalletLedgerEntryRepository
{
    Task AddLedgerEntryAsync(string ledgerEntryId, string walletTransactionId, string walletId, WalletLedgerEntryDirection direction, WalletBalanceType balanceType, decimal amount, CancellationToken cancellationToken = default);
    Task AddLedgerEntryAsync(WalletLedgerEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListLedgerEntryIdsByWalletAsync(string walletId, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListLedgerEntryIdsByWalletTransactionAsync(string walletTransactionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WalletLedgerEntry>> ListLedgerEntriesByWalletTransactionAsync(string walletTransactionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListLedgerEntryIdsByReferencesAsync(string? campaignId = null, string? deliveryId = null, string? withdrawalRequestId = null, CancellationToken cancellationToken = default);
}
