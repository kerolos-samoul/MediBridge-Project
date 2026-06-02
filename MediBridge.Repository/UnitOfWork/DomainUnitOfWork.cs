using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediBridge.Repository.UnitOfWork;

public sealed class DomainUnitOfWork : IDomainUnitOfWork
{
    private readonly MediBridgeDbContext context;

    public DomainUnitOfWork(
        MediBridgeDbContext context,
        ICampaignRepository campaigns,
        IMessageQueueRepository messageQueues,
        IDeliveryRepository deliveries,
        IWalletRepository wallets,
        IWalletTransactionRepository walletTransactions,
        IWalletLedgerEntryRepository walletLedgerEntries,
        IStoredFileRepository storedFiles,
        IPolicyHistoryRepository policyHistory,
        IAuditEventRepository auditEvents)
    {
        this.context = context;
        Campaigns = campaigns;
        MessageQueues = messageQueues;
        Deliveries = deliveries;
        Wallets = wallets;
        WalletTransactions = walletTransactions;
        WalletLedgerEntries = walletLedgerEntries;
        StoredFiles = storedFiles;
        PolicyHistory = policyHistory;
        AuditEvents = auditEvents;
    }

    public ICampaignRepository Campaigns { get; }
    public IMessageQueueRepository MessageQueues { get; }
    public IDeliveryRepository Deliveries { get; }
    public IWalletRepository Wallets { get; }
    public IWalletTransactionRepository WalletTransactions { get; }
    public IWalletLedgerEntryRepository WalletLedgerEntries { get; }
    public IStoredFileRepository StoredFiles { get; }
    public IPolicyHistoryRepository PolicyHistory { get; }
    public IAuditEventRepository AuditEvents { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return context.SaveChangesAsync(cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        // Persist staged wallet balance, transaction, and ledger changes in the same database transaction.
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        // Keep returned results tied to a fully committed unit of work.
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
