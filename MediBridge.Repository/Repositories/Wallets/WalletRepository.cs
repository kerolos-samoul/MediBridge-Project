using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Wallets;

public sealed class WalletRepository : IWalletRepository
{
    private readonly MediBridgeDbContext context;

    public WalletRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddWalletAsync(string walletId, WalletOwnerType ownerType, string ownerId, string? ownerUserId, CancellationToken cancellationToken = default)
    {
        await context.Wallets.AddAsync(new Wallet
        {
            Id = walletId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            OwnerUserId = ownerUserId,
            AvailableBalance = MoneyRules.EnsureValid(0m, nameof(Wallet.AvailableBalance)),
            ReservedBalance = MoneyRules.EnsureValid(0m, nameof(Wallet.ReservedBalance))
        }, cancellationToken);
    }

    public Task<string?> FindActiveWalletIdByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets
            .Where(wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId && !wallet.IsDeleted)
            .Select(wallet => wallet.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<string?> FindWalletIdByOwnerIncludingDeletedAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets
            .IgnoreQueryFilters()
            .Where(wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId)
            .Select(wallet => wallet.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task StageAvailableBalanceChangeAsync(string walletId, decimal amountDelta, CancellationToken cancellationToken = default)
    {
        MoneyRules.EnsureValid(amountDelta, nameof(amountDelta), allowNegative: true);
        var wallet = await context.Wallets.FirstOrDefaultAsync(candidate => candidate.Id == walletId, cancellationToken)
            ?? throw new InvalidOperationException($"Wallet '{walletId}' was not found.");

        var resultingBalance = wallet.AvailableBalance + amountDelta;
        MoneyRules.EnsureValid(resultingBalance, nameof(wallet.AvailableBalance));
        wallet.AvailableBalance = resultingBalance;
        wallet.UpdatedAtUtc = DateTime.UtcNow;
    }

    public async Task StageReservedBalanceChangeAsync(string walletId, decimal amountDelta, CancellationToken cancellationToken = default)
    {
        MoneyRules.EnsureValid(amountDelta, nameof(amountDelta), allowNegative: true);
        var wallet = await context.Wallets.FirstOrDefaultAsync(candidate => candidate.Id == walletId, cancellationToken)
            ?? throw new InvalidOperationException($"Wallet '{walletId}' was not found.");

        var resultingBalance = wallet.ReservedBalance + amountDelta;
        MoneyRules.EnsureValid(resultingBalance, nameof(wallet.ReservedBalance));
        wallet.ReservedBalance = resultingBalance;
        wallet.UpdatedAtUtc = DateTime.UtcNow;
    }

    public Task<bool> ActiveWalletExistsAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets.AnyAsync(wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId && !wallet.IsDeleted, cancellationToken);
    }
}
