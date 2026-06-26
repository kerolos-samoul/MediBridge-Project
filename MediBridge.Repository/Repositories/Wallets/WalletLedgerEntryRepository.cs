using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Wallets;

public sealed class WalletLedgerEntryRepository : IWalletLedgerEntryRepository
{
    private readonly MediBridgeDbContext context;

    public WalletLedgerEntryRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddLedgerEntryAsync(string ledgerEntryId, string walletTransactionId, string walletId, WalletLedgerEntryDirection direction, WalletBalanceType balanceType, decimal amount, CancellationToken cancellationToken = default)
    {
        await context.WalletLedgerEntries.AddAsync(new WalletLedgerEntry
        {
            Id = ledgerEntryId,
            WalletTransactionId = walletTransactionId,
            WalletId = walletId,
            Direction = direction,
            BalanceType = balanceType,
            Amount = MoneyRules.EnsurePositive(amount, nameof(amount))
        }, cancellationToken);
    }

    public async Task AddLedgerEntryAsync(WalletLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Amount = MoneyRules.EnsurePositive(entry.Amount, nameof(entry.Amount));
        await context.WalletLedgerEntries.AddAsync(entry, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListLedgerEntryIdsByWalletAsync(string walletId, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default)
    {
        var query = context.WalletLedgerEntries.Where(entry => entry.WalletId == walletId);
        if (createdFromUtc is not null)
        {
            query = query.Where(entry => entry.CreatedAtUtc >= createdFromUtc);
        }

        if (createdToUtc is not null)
        {
            query = query.Where(entry => entry.CreatedAtUtc <= createdToUtc);
        }

        return await query
            .OrderBy(entry => entry.CreatedAtUtc)
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListLedgerEntryIdsByWalletTransactionAsync(string walletTransactionId, CancellationToken cancellationToken = default)
    {
        return await context.WalletLedgerEntries
            .Where(entry => entry.WalletTransactionId == walletTransactionId)
            .OrderBy(entry => entry.CreatedAtUtc)
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WalletLedgerEntry>> ListLedgerEntriesByWalletTransactionAsync(string walletTransactionId, CancellationToken cancellationToken = default)
    {
        return await context.WalletLedgerEntries
            .Where(entry => entry.WalletTransactionId == walletTransactionId)
            .OrderBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListLedgerEntryIdsByReferencesAsync(string? campaignId = null, string? deliveryId = null, string? withdrawalRequestId = null, CancellationToken cancellationToken = default)
    {
        var query = context.WalletLedgerEntries.AsQueryable();
        if (campaignId is not null)
        {
            query = query.Where(entry => entry.CampaignId == campaignId);
        }

        if (deliveryId is not null)
        {
            query = query.Where(entry => entry.MessageDeliveryId == deliveryId);
        }

        if (withdrawalRequestId is not null)
        {
            query = query.Where(entry => entry.WithdrawalRequestId == withdrawalRequestId);
        }

        return await query
            .OrderBy(entry => entry.CreatedAtUtc)
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }
}
