using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Wallets;

public interface IWalletRepository
{
    Task AddWalletAsync(string walletId, WalletOwnerType ownerType, string ownerId, string? ownerUserId, CancellationToken cancellationToken = default);
    Task<string?> FindActiveWalletIdByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<Wallet?> FindActiveWalletByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<Wallet?> FindActiveWalletForUpdateAsync(string walletId, CancellationToken cancellationToken = default);
    Task<Wallet?> FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<Wallet> GetOrCreateActiveWalletForUpdateAsync(string walletId, WalletOwnerType ownerType, string ownerId, string? ownerUserId, DateTime createdAtUtc, CancellationToken cancellationToken = default);
    Task<string?> FindWalletIdByOwnerIncludingDeletedAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<(decimal AvailableBalance, decimal ReservedBalance)?> GetActiveWalletBalancesAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task StageAvailableBalanceChangeAsync(string walletId, decimal amountDelta, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
    Task StageReservedBalanceChangeAsync(string walletId, decimal amountDelta, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
    Task<bool> ActiveWalletExistsAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
}
