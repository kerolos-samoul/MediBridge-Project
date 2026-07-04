using System.Diagnostics;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediBridge.Services.Services;

public sealed class DeliveryExpiryService : IDeliveryExpiryService
{
    private const int PageSize = 100;
    private const string SafeFailureSummary = "One or more delivery expiry candidates failed.";
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IEgyptBusinessClock businessClock;
    private readonly DeliveryJobRunTracker? runTracker;
    private readonly ILogger<DeliveryExpiryService> logger;

    public DeliveryExpiryService(IDomainUnitOfWork domainUnitOfWork, IEgyptBusinessClock businessClock)
        : this(domainUnitOfWork, businessClock, null, NullLogger<DeliveryExpiryService>.Instance)
    {
    }

    public DeliveryExpiryService(
        IDomainUnitOfWork domainUnitOfWork,
        IEgyptBusinessClock businessClock,
        DeliveryJobRunTracker? runTracker,
        ILogger<DeliveryExpiryService> logger)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.businessClock = businessClock;
        this.runTracker = runTracker;
        this.logger = logger;
    }

    public async Task<DeliveryJobResultDto> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = businessClock.Capture();
        var elapsed = Stopwatch.StartNew();
        var progress = new DeliveryJobRunProgress();
        var handle = runTracker is null
            ? null
            : await runTracker.StartAsync(DeliveryJobType.ExpiryCleaner, snapshot.BusinessDateEgypt, snapshot.UtcNow, cancellationToken);
        try
        {
            var result = await RunCoreAsync(snapshot, elapsed, progress, cancellationToken);
            if (handle is not null)
            {
                await runTracker!.CompleteAsync(
                    handle,
                    result.Outcome,
                    progress.ToCounters(),
                    result.CompletedAtUtc,
                    result.FailureSummary,
                    cancellationToken);
            }

            logger.LogInformation(
                "Delivery expiry run {RunId} finished for {BusinessDateEgypt} with {Status}; examined {ExaminedCount}, expired {ExpiredCount}, skipped {SkippedCount}, failed {FailedCount}.",
                handle?.RunId,
                result.BusinessDateEgypt,
                result.Outcome,
                result.ExaminedCount,
                result.ExpiredCount,
                result.SkippedCount,
                result.FailedCount);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompleteExceptionalRunAsync(handle, DeliveryJobRunStatus.Interrupted, "The delivery expiry run was interrupted.", snapshot, elapsed, progress);
            logger.LogWarning("Delivery expiry run {RunId} was interrupted for {BusinessDateEgypt}.", handle?.RunId, snapshot.BusinessDateEgypt);
            throw;
        }
        catch
        {
            progress.RecordFailed();
            await CompleteExceptionalRunAsync(handle, DeliveryJobRunStatus.Failed, "The delivery expiry cycle failed.", snapshot, elapsed, progress);
            logger.LogError("Delivery expiry run {RunId} failed for {BusinessDateEgypt}.", handle?.RunId, snapshot.BusinessDateEgypt);
            throw;
        }
    }

    private async Task<DeliveryJobResultDto> RunCoreAsync(
        EgyptBusinessTimeSnapshot snapshot,
        Stopwatch elapsed,
        DeliveryJobRunProgress progress,
        CancellationToken cancellationToken)
    {
        OverdueDeliveryCursor? cursor = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await domainUnitOfWork.Deliveries.ListOverduePageAsync(
                snapshot.BusinessDateEgypt,
                cursor,
                PageSize,
                cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var candidate in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.RecordExamined();
                var outcome = await ProcessCandidateAsync(candidate.Id, snapshot, cancellationToken);
                switch (outcome)
                {
                    case CandidateOutcome.Expired:
                        progress.RecordExpired();
                        break;
                    case CandidateOutcome.Skipped:
                        progress.RecordSkipped();
                        break;
                    case CandidateOutcome.Failed:
                        progress.RecordFailed();
                        break;
                }
            }

            var last = page[^1];
            cursor = new OverdueDeliveryCursor(last.DeliveryDateEgypt, last.CreatedAtUtc, last.Id);
            if (page.Count < PageSize)
            {
                break;
            }
        }

        elapsed.Stop();
        return new DeliveryJobResultDto(
            snapshot.BusinessDateEgypt,
            snapshot.UtcNow,
            snapshot.UtcNow.Add(elapsed.Elapsed),
            ClassifyOutcome(progress.ExpiredCount, progress.FailedCount),
            progress.ExaminedCount,
            progress.ExpiredCount,
            progress.SkippedCount,
            progress.FailedCount,
            progress.FailedCount == 0 ? null : SafeFailureSummary);
    }

    private Task CompleteExceptionalRunAsync(
        DeliveryJobRunHandle? handle,
        DeliveryJobRunStatus status,
        string summary,
        EgyptBusinessTimeSnapshot snapshot,
        Stopwatch elapsed,
        DeliveryJobRunProgress progress)
    {
        elapsed.Stop();
        return handle is null || runTracker is null
            ? Task.CompletedTask
            : runTracker.CompleteAsync(
                handle,
                status,
                progress.ToCounters(),
                snapshot.UtcNow.Add(elapsed.Elapsed),
                summary,
                CancellationToken.None);
    }

    private async Task<CandidateOutcome> ProcessCandidateAsync(
        string deliveryId,
        EgyptBusinessTimeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        DoctorAdDelivery? attemptedDelivery = null;
        try
        {
            return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
            {
                attemptedDelivery = await domainUnitOfWork.Deliveries.FindActiveReservedForUpdateAsync(
                    deliveryId,
                    transactionCancellationToken);
                if (attemptedDelivery is null || attemptedDelivery.DeliveryDateEgypt >= snapshot.BusinessDateEgypt)
                {
                    return CandidateOutcome.Skipped;
                }

                var releaseKey = DeliveryFinancialOperationKeys.ForRelease(attemptedDelivery.Id);
                var existingRelease = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
                    WalletTransactionType.Release,
                    releaseKey,
                    transactionCancellationToken);
                if (existingRelease is not null)
                {
                    throw new InvalidOperationException("The active delivery has conflicting release evidence.");
                }

                var wallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(
                    WalletOwnerType.Company,
                    attemptedDelivery.CompanyId,
                    transactionCancellationToken)
                    ?? throw new InvalidOperationException("The active company wallet is unavailable.");
                if (!string.Equals(wallet.Currency, "EGP", StringComparison.Ordinal) ||
                    wallet.ReservedBalance < attemptedDelivery.ReservedAmount)
                {
                    throw new InvalidOperationException("The company reservation cannot be released consistently.");
                }

                attemptedDelivery.MarkExpiredAndReleased(snapshot.UtcNow);
                await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(
                    wallet.Id,
                    -attemptedDelivery.ReservedAmount,
                    snapshot.UtcNow,
                    transactionCancellationToken);
                await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(
                    wallet.Id,
                    attemptedDelivery.ReservedAmount,
                    snapshot.UtcNow,
                    transactionCancellationToken);

                var transactionId = Guid.NewGuid().ToString("N");
                await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
                {
                    Id = transactionId,
                    WalletId = wallet.Id,
                    OperationType = WalletTransactionType.Release,
                    IdempotencyKey = releaseKey,
                    Amount = attemptedDelivery.ReservedAmount,
                    RelatedDeliveryId = attemptedDelivery.Id,
                    Description = "Delivery reservation released after expiry.",
                    CreatedAtUtc = snapshot.UtcNow
                }, transactionCancellationToken);
                await AddReleaseLedgerEntryAsync(
                    attemptedDelivery,
                    wallet,
                    transactionId,
                    releaseKey,
                    WalletLedgerEntryDirection.Debit,
                    WalletBalanceType.Reserved,
                    snapshot.UtcNow,
                    transactionCancellationToken);
                await AddReleaseLedgerEntryAsync(
                    attemptedDelivery,
                    wallet,
                    transactionId,
                    releaseKey,
                    WalletLedgerEntryDirection.Credit,
                    WalletBalanceType.Available,
                    snapshot.UtcNow,
                    transactionCancellationToken);

                return CandidateOutcome.Expired;
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await IsCompletedReplayAsync(deliveryId, attemptedDelivery, cancellationToken)
                ? CandidateOutcome.Skipped
                : CandidateOutcome.Failed;
        }
    }

    private Task AddReleaseLedgerEntryAsync(
        DoctorAdDelivery delivery,
        Wallet wallet,
        string transactionId,
        string releaseKey,
        WalletLedgerEntryDirection direction,
        WalletBalanceType balanceType,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        return domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletTransactionId = transactionId,
            WalletId = wallet.Id,
            Direction = direction,
            BalanceType = balanceType,
            Amount = delivery.ReservedAmount,
            Currency = "EGP",
            CampaignId = delivery.CampaignId,
            MessageDeliveryId = delivery.Id,
            CompanyId = delivery.CompanyId,
            IdempotencyKey = releaseKey,
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);
    }

    private async Task<bool> IsCompletedReplayAsync(
        string deliveryId,
        DoctorAdDelivery? attemptedDelivery,
        CancellationToken cancellationToken)
    {
        if (attemptedDelivery is null)
        {
            return false;
        }

        try
        {
            return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
            {
                var stillActive = await domainUnitOfWork.Deliveries.FindActiveReservedForUpdateAsync(
                    deliveryId,
                    transactionCancellationToken);
                if (stillActive is not null)
                {
                    return false;
                }

                var completedDelivery = await domainUnitOfWork.Deliveries.FindReservationReplayAsync(
                    attemptedDelivery.DoctorId,
                    attemptedDelivery.DeliveryDateEgypt,
                    attemptedDelivery.CampaignId,
                    transactionCancellationToken);
                if (completedDelivery is null ||
                    !string.Equals(completedDelivery.Id, deliveryId, StringComparison.Ordinal) ||
                    !string.Equals(completedDelivery.CompanyId, attemptedDelivery.CompanyId, StringComparison.Ordinal) ||
                    completedDelivery.Status != DeliveryStatus.Expired ||
                    completedDelivery.ReservationStatus != ReservationStatus.Released ||
                    completedDelivery.ReservedAmount != attemptedDelivery.ReservedAmount)
                {
                    return false;
                }

                var releaseKey = DeliveryFinancialOperationKeys.ForRelease(deliveryId);
                var transaction = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
                    WalletTransactionType.Release,
                    releaseKey,
                    transactionCancellationToken);
                if (transaction is null ||
                    transaction.OperationType != WalletTransactionType.Release ||
                    !string.Equals(transaction.IdempotencyKey, releaseKey, StringComparison.Ordinal) ||
                    !string.Equals(transaction.RelatedDeliveryId, deliveryId, StringComparison.Ordinal) ||
                    transaction.Amount != completedDelivery.ReservedAmount)
                {
                    return false;
                }

                var entries = await domainUnitOfWork.WalletLedgerEntries.ListLedgerEntriesByWalletTransactionAsync(
                    transaction.Id,
                    transactionCancellationToken);
                return entries.Count == 2 &&
                    entries.Any(entry => IsMatchingReleaseEntry(entry, transaction, completedDelivery, releaseKey, WalletBalanceType.Reserved, WalletLedgerEntryDirection.Debit)) &&
                    entries.Any(entry => IsMatchingReleaseEntry(entry, transaction, completedDelivery, releaseKey, WalletBalanceType.Available, WalletLedgerEntryDirection.Credit));
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMatchingReleaseEntry(
        WalletLedgerEntry entry,
        WalletTransaction transaction,
        DeliveryReservationReplayReadModel delivery,
        string releaseKey,
        WalletBalanceType balanceType,
        WalletLedgerEntryDirection direction)
    {
        return string.Equals(entry.WalletTransactionId, transaction.Id, StringComparison.Ordinal) &&
            string.Equals(entry.WalletId, transaction.WalletId, StringComparison.Ordinal) &&
            entry.BalanceType == balanceType &&
            entry.Direction == direction &&
            entry.Amount == delivery.ReservedAmount &&
            string.Equals(entry.Currency, "EGP", StringComparison.Ordinal) &&
            string.Equals(entry.MessageDeliveryId, delivery.Id, StringComparison.Ordinal) &&
            string.Equals(entry.CampaignId, delivery.CampaignId, StringComparison.Ordinal) &&
            entry.DoctorId is null &&
            string.Equals(entry.CompanyId, delivery.CompanyId, StringComparison.Ordinal) &&
            string.Equals(entry.IdempotencyKey, releaseKey, StringComparison.Ordinal);
    }

    private static DeliveryJobRunStatus ClassifyOutcome(int expiredCount, int failedCount)
    {
        if (failedCount == 0)
        {
            return DeliveryJobRunStatus.Succeeded;
        }

        return expiredCount > 0
            ? DeliveryJobRunStatus.PartiallySucceeded
            : DeliveryJobRunStatus.Failed;
    }

    private enum CandidateOutcome
    {
        Expired,
        Skipped,
        Failed
    }
}
