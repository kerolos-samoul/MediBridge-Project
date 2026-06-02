using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Wallets;

namespace MediBridge.Core.Interfaces;

public interface IDomainUnitOfWork
{
    ICampaignRepository Campaigns { get; }
    IMessageQueueRepository MessageQueues { get; }
    IDeliveryRepository Deliveries { get; }
    IWalletRepository Wallets { get; }
    IWalletTransactionRepository WalletTransactions { get; }
    IWalletLedgerEntryRepository WalletLedgerEntries { get; }
    IStoredFileRepository StoredFiles { get; }
    IPolicyHistoryRepository PolicyHistory { get; }
    IAuditEventRepository AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
