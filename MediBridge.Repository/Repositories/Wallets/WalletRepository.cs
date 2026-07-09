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

    public Task<Wallet?> FindActiveWalletByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets.FirstOrDefaultAsync(
            wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId && !wallet.IsDeleted,
            cancellationToken);
    }

    public Task<Wallet?> FindActiveWalletForUpdateAsync(string walletId, CancellationToken cancellationToken = default)
    {
        return context.Wallets
            .FromSqlInterpolated($"SELECT * FROM [Wallets] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {walletId} AND [IsDeleted] = 0")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Wallet?> FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets
            .FromSqlInterpolated($"SELECT * FROM [Wallets] WITH (UPDLOCK, ROWLOCK) WHERE [OwnerType] = {(int)ownerType} AND [OwnerId] = {ownerId} AND [IsDeleted] = 0")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Wallet> GetOrCreateActiveWalletForUpdateAsync(
        string walletId,
        WalletOwnerType ownerType,
        string ownerId,
        string? ownerUserId,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
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

        wallet = new Wallet
        {
            Id = walletId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            OwnerUserId = ownerUserId,
            AvailableBalance = 0m,
            ReservedBalance = 0m,
            Currency = "EGP",
            CreatedAtUtc = createdAtUtc
        };
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

    public async Task<(decimal AvailableBalance, decimal ReservedBalance)?> GetActiveWalletBalancesAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        var wallet = await context.Wallets
            .Where(candidate => candidate.OwnerType == ownerType && candidate.OwnerId == ownerId && !candidate.IsDeleted)
            .Select(candidate => new { candidate.AvailableBalance, candidate.ReservedBalance })
            .FirstOrDefaultAsync(cancellationToken);

        return wallet is null ? null : (wallet.AvailableBalance, wallet.ReservedBalance);
    }

    public async Task StageAvailableBalanceChangeAsync(string walletId, decimal amountDelta, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        MoneyRules.EnsureValid(amountDelta, nameof(amountDelta), allowNegative: true);
        var wallet = context.Wallets.Local.FirstOrDefault(candidate => candidate.Id == walletId)
            ?? await context.Wallets.FirstOrDefaultAsync(candidate => candidate.Id == walletId, cancellationToken)
            ?? throw new InvalidOperationException($"Wallet '{walletId}' was not found.");

        var resultingBalance = wallet.AvailableBalance + amountDelta;
        MoneyRules.EnsureValid(resultingBalance, nameof(wallet.AvailableBalance));
        wallet.AvailableBalance = resultingBalance;
        wallet.UpdatedAtUtc = updatedAtUtc;
    }

    public async Task StageReservedBalanceChangeAsync(string walletId, decimal amountDelta, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        MoneyRules.EnsureValid(amountDelta, nameof(amountDelta), allowNegative: true);
        var wallet = await context.Wallets.FirstOrDefaultAsync(candidate => candidate.Id == walletId, cancellationToken)
            ?? throw new InvalidOperationException($"Wallet '{walletId}' was not found.");

        var resultingBalance = wallet.ReservedBalance + amountDelta;
        MoneyRules.EnsureValid(resultingBalance, nameof(wallet.ReservedBalance));
        wallet.ReservedBalance = resultingBalance;
        wallet.UpdatedAtUtc = updatedAtUtc;
    }

    public Task<bool> ActiveWalletExistsAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
    {
        return context.Wallets.AnyAsync(wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId && !wallet.IsDeleted, cancellationToken);
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The wallet mutation timestamp must be UTC.", parameterName);
        }
    }
}
