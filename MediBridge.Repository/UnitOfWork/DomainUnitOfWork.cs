using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Payments;
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
        IProfileRepository profiles,
        IMessageQueueRepository messageQueues,
        IDeliveryRepository deliveries,
        IDeliveryInteractionRepository deliveryInteractions,
        IDeliveryJobRunRepository deliveryJobRuns,
        IDeliveryRecoveryDispatchRepository deliveryRecoveryDispatches,
        IWalletRepository wallets,
        IWalletTransactionRepository walletTransactions,
        IWalletLedgerEntryRepository walletLedgerEntries,
        IPaymentRepository payments,
        IStoredFileRepository storedFiles,
        IFileReviewRepository fileReviews,
        IFileAccessGrantAuditRepository fileAccessGrantAudits,
        IPolicyHistoryRepository policyHistory,
        IAuditEventRepository auditEvents)
    {
        this.context = context;
        Campaigns = campaigns;
        Profiles = profiles;
        MessageQueues = messageQueues;
        Deliveries = deliveries;
        DeliveryInteractions = deliveryInteractions;
        DeliveryJobRuns = deliveryJobRuns;
        DeliveryRecoveryDispatches = deliveryRecoveryDispatches;
        Wallets = wallets;
        WalletTransactions = walletTransactions;
        WalletLedgerEntries = walletLedgerEntries;
        Payments = payments;
        StoredFiles = storedFiles;
        FileReviews = fileReviews;
        FileAccessGrantAudits = fileAccessGrantAudits;
        PolicyHistory = policyHistory;
        AuditEvents = auditEvents;
    }

    public ICampaignRepository Campaigns { get; }
    public IProfileRepository Profiles { get; }
    public IMessageQueueRepository MessageQueues { get; }
    public IDeliveryRepository Deliveries { get; }
    public IDeliveryInteractionRepository DeliveryInteractions { get; }
    public IDeliveryJobRunRepository DeliveryJobRuns { get; }
    public IDeliveryRecoveryDispatchRepository DeliveryRecoveryDispatches { get; }
    public IWalletRepository Wallets { get; }
    public IWalletTransactionRepository WalletTransactions { get; }
    public IWalletLedgerEntryRepository WalletLedgerEntries { get; }
    public IPaymentRepository Payments { get; }
    public IStoredFileRepository StoredFiles { get; }
    public IFileReviewRepository FileReviews { get; }
    public IFileAccessGrantAuditRepository FileAccessGrantAudits { get; }
    public IPolicyHistoryRepository PolicyHistory { get; }
    public IAuditEventRepository AuditEvents { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return context.SaveChangesAsync(cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await ExecuteTransactionCoreAsync(
            async transactionCancellationToken =>
            {
                await operation(transactionCancellationToken);
                return true;
            },
            clearTrackingAfterSuccess: false,
            cancellationToken);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        return ExecuteTransactionCoreAsync(operation, clearTrackingAfterSuccess: false, cancellationToken);
    }

    public Task<T> ExecuteIsolatedInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteTransactionCoreAsync(operation, clearTrackingAfterSuccess: true, cancellationToken);
    }

    private async Task<T> ExecuteTransactionCoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        bool clearTrackingAfterSuccess,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completed = false;
        try
        {
            T result;
            await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    result = await operation(cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception operationException)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        var combinedFailure = new AggregateException(
                            "The transaction operation and its rollback both failed.",
                            operationException,
                            rollbackException);
                        if (operationException is OperationCanceledException && cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(
                                "The transaction was cancelled and rollback also failed.",
                                combinedFailure,
                                cancellationToken);
                        }

                        throw combinedFailure;
                    }

                    throw;
                }
            }

            completed = true;
            return result;
        }
        finally
        {
            if (clearTrackingAfterSuccess || !completed)
            {
                context.ChangeTracker.Clear();
            }
        }
    }
}
