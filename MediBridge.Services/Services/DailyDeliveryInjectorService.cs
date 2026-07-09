using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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

public sealed class DailyDeliveryInjectorService : IDailyDeliveryInjectorService
{
    private static readonly TimeSpan EarliestInjectionTime = new(0, 5, 0);
    private const string SafeFailureSummary = "One or more daily delivery candidates failed.";
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IEgyptBusinessClock businessClock;
    private readonly DeliverySettlementSnapshotCalculator snapshotCalculator;
    private readonly DeliveryCandidateEligibilityPolicy eligibilityPolicy;
    private readonly int batchSize;
    private readonly DeliveryJobRunTracker? runTracker;
    private readonly ILogger<DailyDeliveryInjectorService> logger;

    public DailyDeliveryInjectorService(
        IDomainUnitOfWork domainUnitOfWork,
        IEgyptBusinessClock businessClock,
        DeliverySettlementSnapshotCalculator snapshotCalculator,
        DeliveryCandidateEligibilityPolicy eligibilityPolicy,
        int batchSize = 100)
        : this(
            domainUnitOfWork,
            businessClock,
            snapshotCalculator,
            eligibilityPolicy,
            batchSize,
            null,
            NullLogger<DailyDeliveryInjectorService>.Instance)
    {
    }

    public DailyDeliveryInjectorService(
        IDomainUnitOfWork domainUnitOfWork,
        IEgyptBusinessClock businessClock,
        DeliverySettlementSnapshotCalculator snapshotCalculator,
        DeliveryCandidateEligibilityPolicy eligibilityPolicy,
        int batchSize,
        DeliveryJobRunTracker? runTracker,
        ILogger<DailyDeliveryInjectorService> logger)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.businessClock = businessClock;
        this.snapshotCalculator = snapshotCalculator;
        this.eligibilityPolicy = eligibilityPolicy;
        this.runTracker = runTracker;
        this.logger = logger;
        this.batchSize = batchSize is >= 1 and <= 1000
            ? batchSize
            : throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be between 1 and 1000.");
    }

    public async Task<DailyInjectorResultDto> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = businessClock.Capture();
        var elapsed = Stopwatch.StartNew();
        var progress = new DeliveryJobRunProgress();
        var handle = runTracker is null
            ? null
            : await runTracker.StartAsync(DeliveryJobType.DailyInjector, snapshot.BusinessDateEgypt, snapshot.UtcNow, cancellationToken);
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
                "Daily injector run {RunId} finished for {BusinessDateEgypt} with {Status}; examined {ExaminedCount}, activated {ActivatedCount}, cancelled {CancelledCount}, skipped {SkippedCount}, failed {FailedCount}.",
                handle?.RunId,
                result.BusinessDateEgypt,
                result.Outcome,
                result.ExaminedCount,
                result.ActivatedCount,
                result.CancelledCount,
                result.SkippedCount,
                result.FailedCount);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompleteExceptionalRunAsync(handle, DeliveryJobRunStatus.Interrupted, "The daily injector run was interrupted.", snapshot, elapsed, progress);
            logger.LogWarning("Daily injector run {RunId} was interrupted for {BusinessDateEgypt}.", handle?.RunId, snapshot.BusinessDateEgypt);
            throw;
        }
        catch
        {
            progress.RecordFailed();
            await CompleteExceptionalRunAsync(handle, DeliveryJobRunStatus.Failed, "The daily injector cycle failed.", snapshot, elapsed, progress);
            logger.LogError("Daily injector run {RunId} failed for {BusinessDateEgypt}.", handle?.RunId, snapshot.BusinessDateEgypt);
            throw;
        }
    }

    private async Task<DailyInjectorResultDto> RunCoreAsync(
        EgyptBusinessTimeSnapshot snapshot,
        Stopwatch elapsed,
        DeliveryJobRunProgress progress,
        CancellationToken cancellationToken)
    {
        if (snapshot.EgyptLocalNow.TimeOfDay < EarliestInjectionTime)
        {
            return Result(snapshot, elapsed, DeliveryJobRunStatus.Deferred, 0, 0, 0, 0, 0, null);
        }

        QueuedDoctorCursor? doctorCursor = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doctors = await domainUnitOfWork.MessageQueues.ListQueuedDoctorsPageAsync(
                doctorCursor,
                batchSize,
                cancellationToken);
            if (doctors.Count == 0)
            {
                break;
            }

            foreach (var doctor in doctors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                QueuedCandidateCursor? candidateCursor = null;
                var stopDoctor = false;
                while (!stopDoctor)
                {
                    var candidates = await domainUnitOfWork.MessageQueues.ListQueuedCandidatesPageAsync(
                        doctor.DoctorId,
                        candidateCursor,
                        batchSize,
                        cancellationToken);
                    if (candidates.Count == 0)
                    {
                        break;
                    }

                    foreach (var candidate in candidates)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress.RecordExamined();
                        CandidateOutcome outcome;
                        if (candidate.CampaignSubmittedAtUtc is null)
                        {
                            outcome = CandidateOutcome.Failed;
                        }
                        else
                        {
                            outcome = await ProcessCandidateAsync(candidate, snapshot, cancellationToken);
                        }

                        switch (outcome)
                        {
                            case CandidateOutcome.Activated:
                                progress.RecordActivated();
                                break;
                            case CandidateOutcome.Cancelled:
                                progress.RecordCancelled();
                                break;
                            case CandidateOutcome.Skipped:
                            case CandidateOutcome.Replay:
                                progress.RecordSkipped();
                                break;
                            case CandidateOutcome.NoCapacity:
                                progress.RecordSkipped();
                                stopDoctor = true;
                                break;
                            case CandidateOutcome.Failed:
                                progress.RecordFailed();
                                break;
                        }

                        if (stopDoctor)
                        {
                            break;
                        }
                    }

                    var last = candidates[^1];
                    candidateCursor = new QueuedCandidateCursor(last.CampaignSubmittedAtUtc, last.Id);
                    if (stopDoctor || candidates.Count < batchSize)
                    {
                        break;
                    }
                }
            }

            doctorCursor = new QueuedDoctorCursor(doctors[^1].DoctorId);
            if (doctors.Count < batchSize)
            {
                break;
            }
        }

        var outcomeStatus = progress.FailedCount == 0
            ? DeliveryJobRunStatus.Succeeded
            : progress.ActivatedCount > 0 || progress.CancelledCount > 0
                ? DeliveryJobRunStatus.PartiallySucceeded
                : DeliveryJobRunStatus.Failed;
        return Result(
            snapshot,
            elapsed,
            outcomeStatus,
            progress.ExaminedCount,
            progress.ActivatedCount,
            progress.CancelledCount,
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
        DeliveryQueueCandidateReadModel candidate,
        EgyptBusinessTimeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
            {
                var doctor = await domainUnitOfWork.Profiles.FindDoctorDeliveryEligibilityForUpdateAsync(
                    candidate.DoctorId,
                    transactionCancellationToken);
                var currentDayCount = doctor is null
                    ? 0
                    : await domainUnitOfWork.Deliveries.CountForDoctorOnDateAsync(
                        candidate.DoctorId,
                        snapshot.BusinessDateEgypt,
                        transactionCancellationToken);
                var queueItem = await domainUnitOfWork.MessageQueues.FindQueuedItemForUpdateAsync(
                    candidate.Id,
                    transactionCancellationToken);
                if (queueItem is null)
                {
                    return await HasCompletedTerminalStateAsync(candidate, snapshot.BusinessDateEgypt, transactionCancellationToken)
                        ? CandidateOutcome.Replay
                        : CandidateOutcome.Failed;
                }

                var campaign = await domainUnitOfWork.Campaigns.FindDeliveryEligibilityForUpdateAsync(
                    queueItem.CampaignId,
                    transactionCancellationToken);
                var preliminary = eligibilityPolicy.Evaluate(
                    doctor,
                    campaign,
                    currentDayCount,
                    decimal.MaxValue,
                    doctor?.PricePerMessage ?? 0m,
                    false);
                if (preliminary.Decision == DeliveryCandidateEligibilityDecision.CancelTerminal)
                {
                    queueItem.MarkCancelled(snapshot.UtcNow);
                    return CandidateOutcome.Cancelled;
                }

                if (preliminary.Decision == DeliveryCandidateEligibilityDecision.KeepQueuedTemporary)
                {
                    return CandidateOutcome.Skipped;
                }

                if (preliminary.Decision == DeliveryCandidateEligibilityDecision.NoCapacity)
                {
                    return CandidateOutcome.NoCapacity;
                }

                if (preliminary.Decision == DeliveryCandidateEligibilityDecision.Failed || doctor is null || campaign is null)
                {
                    return CandidateOutcome.Failed;
                }

                if (await domainUnitOfWork.Deliveries.HasOverdueActiveReservedForCompanyAsync(
                    campaign.CompanyId,
                    snapshot.BusinessDateEgypt,
                    transactionCancellationToken))
                {
                    return CandidateOutcome.Skipped;
                }

                var policy = await domainUnitOfWork.PolicyHistory.FindSingleEffectivePlatformFeePolicyAsync(
                    snapshot.UtcNow,
                    transactionCancellationToken);
                var settlement = snapshotCalculator.Calculate(
                    doctor.PricePerMessage!.Value,
                    policy is null ? [] : [policy]);

                var existingDeliveryId = await domainUnitOfWork.Deliveries.FindDeliveryIdAsync(
                    doctor.DoctorId,
                    snapshot.BusinessDateEgypt,
                    campaign.CampaignId,
                    transactionCancellationToken);
                if (existingDeliveryId is not null)
                {
                    return CandidateOutcome.Failed;
                }

                var deliveryId = DeriveDeliveryId(snapshot.BusinessDateEgypt, queueItem.Id);
                var reserveKey = DeliveryFinancialOperationKeys.ForReserve(deliveryId);
                var replay = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
                    WalletTransactionType.Reserve,
                    reserveKey,
                    transactionCancellationToken);
                if (replay is not null)
                {
                    return CandidateOutcome.Failed;
                }

                var wallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(
                    WalletOwnerType.Company,
                    campaign.CompanyId,
                    transactionCancellationToken);
                if (wallet is null || !string.Equals(wallet.Currency, "EGP", StringComparison.Ordinal))
                {
                    return CandidateOutcome.Failed;
                }

                var finalEligibility = eligibilityPolicy.Evaluate(
                    doctor,
                    campaign,
                    currentDayCount,
                    wallet.AvailableBalance,
                    settlement.PricePerMessage,
                    false);
                if (finalEligibility.Decision == DeliveryCandidateEligibilityDecision.KeepQueuedInsufficientFunds)
                {
                    return CandidateOutcome.Skipped;
                }

                if (finalEligibility.Decision != DeliveryCandidateEligibilityDecision.Eligible)
                {
                    return CandidateOutcome.Failed;
                }

                await domainUnitOfWork.Deliveries.AddDeliveryAsync(
                    deliveryId,
                    doctor.DoctorId,
                    campaign.CampaignId,
                    campaign.CompanyId,
                    snapshot.BusinessDateEgypt,
                    settlement.PricePerMessage,
                    settlement.PlatformFeePercent,
                    settlement.PlatformFeeAmount,
                    settlement.DoctorEarnings,
                    settlement.ReservedAmount,
                    snapshot.UtcNow,
                    transactionCancellationToken);
                queueItem.MarkActivated(snapshot.UtcNow);
                await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(
                    wallet.Id,
                    -settlement.ReservedAmount,
                    snapshot.UtcNow,
                    transactionCancellationToken);
                await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(
                    wallet.Id,
                    settlement.ReservedAmount,
                    snapshot.UtcNow,
                    transactionCancellationToken);

                var transactionId = Guid.NewGuid().ToString("N");
                await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
                {
                    Id = transactionId,
                    WalletId = wallet.Id,
                    OperationType = WalletTransactionType.Reserve,
                    IdempotencyKey = reserveKey,
                    Amount = settlement.ReservedAmount,
                    RelatedDeliveryId = deliveryId,
                    Description = "Company funds reserved for daily delivery activation.",
                    CreatedAtUtc = snapshot.UtcNow
                }, transactionCancellationToken);
                await AddReserveLedgerEntryAsync(
                    wallet.Id,
                    transactionId,
                    reserveKey,
                    deliveryId,
                    campaign,
                    doctor.DoctorId,
                    settlement.ReservedAmount,
                    WalletBalanceType.Available,
                    WalletLedgerEntryDirection.Debit,
                    snapshot.UtcNow,
                    transactionCancellationToken);
                await AddReserveLedgerEntryAsync(
                    wallet.Id,
                    transactionId,
                    reserveKey,
                    deliveryId,
                    campaign,
                    doctor.DoctorId,
                    settlement.ReservedAmount,
                    WalletBalanceType.Reserved,
                    WalletLedgerEntryDirection.Credit,
                    snapshot.UtcNow,
                    transactionCancellationToken);

                return CandidateOutcome.Activated;
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await IsCompletedReplayAsync(candidate, snapshot.BusinessDateEgypt, cancellationToken)
                ? CandidateOutcome.Replay
                : CandidateOutcome.Failed;
        }
    }

    private Task AddReserveLedgerEntryAsync(
        string walletId,
        string transactionId,
        string reserveKey,
        string deliveryId,
        LockedCampaignCompanyEligibilityReadModel campaign,
        string doctorId,
        decimal amount,
        WalletBalanceType balanceType,
        WalletLedgerEntryDirection direction,
        DateTime createdAtUtc,
        CancellationToken cancellationToken) =>
        domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletTransactionId = transactionId,
            WalletId = walletId,
            Direction = direction,
            BalanceType = balanceType,
            Amount = amount,
            Currency = "EGP",
            CampaignId = campaign.CampaignId,
            MessageDeliveryId = deliveryId,
            DoctorId = doctorId,
            CompanyId = campaign.CompanyId,
            IdempotencyKey = reserveKey,
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);

    private async Task<bool> IsCompletedReplayAsync(
        DeliveryQueueCandidateReadModel candidate,
        DateOnly businessDateEgypt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
            {
                return await HasCompletedTerminalStateAsync(candidate, businessDateEgypt, transactionCancellationToken);
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

    private async Task<bool> HasCompletedTerminalStateAsync(
        DeliveryQueueCandidateReadModel candidate,
        DateOnly businessDateEgypt,
        CancellationToken cancellationToken)
    {
        var queueItem = await domainUnitOfWork.MessageQueues.FindQueueItemAsync(
            candidate.CampaignId,
            candidate.DoctorId,
            cancellationToken);
        if (queueItem is null || !string.Equals(queueItem.Id, candidate.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (queueItem.Status == QueueItemStatus.Cancelled)
        {
            return true;
        }

        if (queueItem.Status != QueueItemStatus.Activated)
        {
            return false;
        }

        var delivery = await domainUnitOfWork.Deliveries.FindReservationReplayAsync(
            candidate.DoctorId,
            businessDateEgypt,
            candidate.CampaignId,
            cancellationToken);
        var expectedDeliveryId = DeriveDeliveryId(businessDateEgypt, candidate.Id);
        if (delivery is null ||
            !string.Equals(delivery.Id, expectedDeliveryId, StringComparison.Ordinal) ||
            delivery.Status != DeliveryStatus.Active ||
            delivery.ReservationStatus != ReservationStatus.Reserved ||
            delivery.ReservedAmount <= 0m)
        {
            return false;
        }

        var reserveKey = DeliveryFinancialOperationKeys.ForReserve(delivery.Id);
        var transaction = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
            WalletTransactionType.Reserve,
            reserveKey,
            cancellationToken);
        if (transaction is null ||
            transaction.OperationType != WalletTransactionType.Reserve ||
            !string.Equals(transaction.IdempotencyKey, reserveKey, StringComparison.Ordinal) ||
            !string.Equals(transaction.RelatedDeliveryId, delivery.Id, StringComparison.Ordinal) ||
            transaction.Amount != delivery.ReservedAmount)
        {
            return false;
        }

        var entries = await domainUnitOfWork.WalletLedgerEntries.ListLedgerEntriesByWalletTransactionAsync(
            transaction.Id,
            cancellationToken);
        return entries.Count == 2 &&
            entries.Any(entry => IsMatchingReserveEntry(
                entry,
                transaction,
                delivery,
                reserveKey,
                WalletBalanceType.Available,
                WalletLedgerEntryDirection.Debit)) &&
            entries.Any(entry => IsMatchingReserveEntry(
                entry,
                transaction,
                delivery,
                reserveKey,
                WalletBalanceType.Reserved,
                WalletLedgerEntryDirection.Credit));
    }

    private static bool IsMatchingReserveEntry(
        WalletLedgerEntry entry,
        WalletTransaction transaction,
        DeliveryReservationReplayReadModel delivery,
        string reserveKey,
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
            string.Equals(entry.DoctorId, delivery.DoctorId, StringComparison.Ordinal) &&
            string.Equals(entry.CompanyId, delivery.CompanyId, StringComparison.Ordinal) &&
            string.Equals(entry.IdempotencyKey, reserveKey, StringComparison.Ordinal);
    }

    private static DailyInjectorResultDto Result(
        EgyptBusinessTimeSnapshot snapshot,
        Stopwatch elapsed,
        DeliveryJobRunStatus outcome,
        int examined,
        int activated,
        int cancelled,
        int skipped,
        int failed,
        string? failureSummary)
    {
        elapsed.Stop();
        return new DailyInjectorResultDto(
            snapshot.BusinessDateEgypt,
            snapshot.UtcNow,
            snapshot.UtcNow.Add(elapsed.Elapsed),
            outcome,
            examined,
            activated,
            cancelled,
            skipped,
            failed,
            failureSummary);
    }

    private static string DeriveDeliveryId(DateOnly businessDateEgypt, string queueItemId)
    {
        var source = Encoding.UTF8.GetBytes($"{businessDateEgypt:yyyy-MM-dd}|{queueItemId}");
        return Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }

    private enum CandidateOutcome
    {
        Activated,
        Cancelled,
        Skipped,
        NoCapacity,
        Replay,
        Failed
    }
}
