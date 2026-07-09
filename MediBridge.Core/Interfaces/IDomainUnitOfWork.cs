using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Payments;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Wallets;

namespace MediBridge.Core.Interfaces;

public interface IDomainUnitOfWork
{
    ICampaignRepository Campaigns { get; }
    IProfileRepository Profiles { get; }
    IMessageQueueRepository MessageQueues { get; }
    IDeliveryRepository Deliveries { get; }
    IDeliveryJobRunRepository DeliveryJobRuns { get; }
    IDeliveryRecoveryDispatchRepository DeliveryRecoveryDispatches { get; }
    IWalletRepository Wallets { get; }
    IWalletTransactionRepository WalletTransactions { get; }
    IWalletLedgerEntryRepository WalletLedgerEntries { get; }
    IPaymentRepository Payments { get; }
    IStoredFileRepository StoredFiles { get; }
    IFileReviewRepository FileReviews { get; }
    IFileAccessGrantAuditRepository FileAccessGrantAudits { get; }
    IPolicyHistoryRepository PolicyHistory { get; }
    IAuditEventRepository AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes one self-contained operation in a transaction and releases all persistence tracking
    /// after completion. The operation must return only scalar, immutable, or otherwise detached data.
    /// </summary>
    Task<T> ExecuteIsolatedInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
