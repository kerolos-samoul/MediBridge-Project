using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Wallets;

public interface IWalletRepository
{
    Task AddWalletAsync(string walletId, WalletOwnerType ownerType, string ownerId, string? ownerUserId, CancellationToken cancellationToken = default);
    Task<string?> FindActiveWalletIdByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task<string?> FindWalletIdByOwnerIncludingDeletedAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
    Task StageAvailableBalanceChangeAsync(string walletId, decimal amountDelta, CancellationToken cancellationToken = default);
    Task StageReservedBalanceChangeAsync(string walletId, decimal amountDelta, CancellationToken cancellationToken = default);
    Task<bool> ActiveWalletExistsAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default);
}
