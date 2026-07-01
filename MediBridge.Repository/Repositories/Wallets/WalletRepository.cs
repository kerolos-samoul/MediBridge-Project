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
        await context.Wallets.AddAsync(CreateWallet(walletId, ownerType, ownerId, ownerUserId), cancellationToken);
    }

    public Task<string?> FindActiveWalletIdByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets
            .Where(wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId && !wallet.IsDeleted)
            .Select(wallet => wallet.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Wallet?> FindActiveWalletByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId && !wallet.IsDeleted, cancellationToken);
    }

    public Task<Wallet?> FindActiveWalletForUpdateAsync(
        WalletOwnerType ownerType,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        return context.Wallets
            .FromSqlInterpolated($"""
                SELECT *
                FROM [Wallets] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [OwnerType] = {(int)ownerType}
                    AND [OwnerId] = {ownerId}
                    AND [IsDeleted] = CAST(0 AS bit)
                """)
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Wallet> GetOrCreateActiveWalletForUpdateAsync(
        string walletId,
        WalletOwnerType ownerType,
        string ownerId,
        string? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var trackedWallet = context.Wallets.Local.FirstOrDefault(
            wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId && !wallet.IsDeleted);
        if (trackedWallet is not null)
        {
            return trackedWallet;
        }

        var wallet = await context.Wallets
            .FromSqlInterpolated($"""
                SELECT *
                FROM [Wallets] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [OwnerType] = {(int)ownerType}
                    AND [OwnerId] = {ownerId}
                    AND [IsDeleted] = CAST(0 AS bit)
                """)
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (wallet is not null)
        {
            return wallet;
        }

        wallet = CreateWallet(walletId, ownerType, ownerId, ownerUserId);
        await context.Wallets.AddAsync(wallet, cancellationToken);
        return wallet;
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
        var wallet = context.Wallets.Local.FirstOrDefault(candidate => candidate.Id == walletId)
            ?? await context.Wallets.FirstOrDefaultAsync(candidate => candidate.Id == walletId, cancellationToken)
            ?? throw new InvalidOperationException($"Wallet '{walletId}' was not found.");

        var resultingBalance = wallet.AvailableBalance + amountDelta;
        MoneyRules.EnsureValid(resultingBalance, nameof(wallet.AvailableBalance));
        wallet.AvailableBalance = resultingBalance;
        wallet.UpdatedAtUtc = DateTime.UtcNow;
    }

    public async Task StageReservedBalanceChangeAsync(string walletId, decimal amountDelta, CancellationToken cancellationToken = default)
    {
        MoneyRules.EnsureValid(amountDelta, nameof(amountDelta), allowNegative: true);
        var wallet = context.Wallets.Local.FirstOrDefault(candidate => candidate.Id == walletId)
            ?? await context.Wallets.FirstOrDefaultAsync(candidate => candidate.Id == walletId, cancellationToken)
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

    private static Wallet CreateWallet(string walletId, WalletOwnerType ownerType, string ownerId, string? ownerUserId)
    {
        return new Wallet
        {
            Id = walletId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            OwnerUserId = ownerUserId,
            AvailableBalance = MoneyRules.EnsureValid(0m, nameof(Wallet.AvailableBalance)),
            ReservedBalance = MoneyRules.EnsureValid(0m, nameof(Wallet.ReservedBalance))
        };
    }
}
