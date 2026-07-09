using System.Reflection;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class DeliveryExpiryServiceTests
{
    [Fact]
    public async Task RunAsync_EmptyPageReturnsSuccessfulZeroCountsFromOneCapturedSnapshot()
    {
        var clock = new RecordingClock(Snapshot());
        var deliveries = Proxy<IDeliveryRepository>((method, arguments) => method.Name switch
        {
            nameof(IDeliveryRepository.ListOverduePageAsync) => Task.FromResult<IReadOnlyList<OverdueDeliveryReadModel>>([]),
            _ => Default(method.ReturnType)
        });
        var service = new DeliveryExpiryService(CreateUnitOfWork(deliveries), clock);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, clock.CaptureCount);
        Assert.Equal(DeliveryJobRunStatus.Succeeded, result.Outcome);
        Assert.Equal((0, 0, 0, 0), (result.ExaminedCount, result.ExpiredCount, result.SkippedCount, result.FailedCount));
        Assert.Null(result.FailureSummary);
    }

    [Fact]
    public async Task RunAsync_PropagatesTheCallerCancellationTokenToDiscovery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var deliveries = Proxy<IDeliveryRepository>((method, arguments) =>
        {
            if (method.Name == nameof(IDeliveryRepository.ListOverduePageAsync))
            {
                Assert.Equal(cancellation.Token, (CancellationToken)arguments![3]!);
                return Task.FromCanceled<IReadOnlyList<OverdueDeliveryReadModel>>(cancellation.Token);
            }

            return Default(method.ReturnType);
        });
        var service = new DeliveryExpiryService(CreateUnitOfWork(deliveries), new RecordingClock(Snapshot()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(cancellation.Token));
    }

    [Fact]
    public async Task RunAsync_IsolatesCandidatesAndReturnsAccurateCountsWithRedactedFailureSummary()
    {
        var snapshot = Snapshot();
        var rows = new[]
        {
            new OverdueDeliveryReadModel("failure", new DateOnly(2026, 7, 1), snapshot.UtcNow.AddDays(-1)),
            new OverdueDeliveryReadModel("success", new DateOnly(2026, 7, 1), snapshot.UtcNow.AddDays(-1)),
            new OverdueDeliveryReadModel("replay", new DateOnly(2026, 7, 1), snapshot.UtcNow.AddDays(-1).AddMinutes(1)),
        };
        var pageCalls = 0;
        var successfulDelivery = ActiveDelivery("success");
        var deliveries = Proxy<IDeliveryRepository>((method, arguments) => method.Name switch
        {
            nameof(IDeliveryRepository.ListOverduePageAsync) => Task.FromResult<IReadOnlyList<OverdueDeliveryReadModel>>(pageCalls++ == 0 ? rows : []),
            nameof(IDeliveryRepository.FindActiveReservedForUpdateAsync) when (string)arguments![0]! == "success" => Task.FromResult<DoctorAdDelivery?>(successfulDelivery),
            nameof(IDeliveryRepository.FindActiveReservedForUpdateAsync) when (string)arguments![0]! == "replay" => Task.FromResult<DoctorAdDelivery?>(null),
            nameof(IDeliveryRepository.FindActiveReservedForUpdateAsync) => throw new InvalidOperationException("Server=db;Password=secret; storage-key signed-url wallet=50 delivery:release:failure\nstack trace"),
            _ => Default(method.ReturnType)
        });
        var wallet = new Wallet { Id = "wallet", OwnerType = WalletOwnerType.Company, OwnerId = "company", AvailableBalance = 100m, ReservedBalance = 50m, Currency = "EGP" };
        var wallets = Proxy<IWalletRepository>((method, _) => method.Name switch
        {
            nameof(IWalletRepository.FindActiveWalletForUpdateByOwnerAsync) => Task.FromResult<Wallet?>(wallet),
            nameof(IWalletRepository.StageAvailableBalanceChangeAsync) => Task.CompletedTask,
            nameof(IWalletRepository.StageReservedBalanceChangeAsync) => Task.CompletedTask,
            _ => Default(method.ReturnType)
        });
        var transactions = Proxy<IWalletTransactionRepository>((method, _) => method.Name switch
        {
            nameof(IWalletTransactionRepository.FindTransactionByIdempotencyAsync) => Task.FromResult<WalletTransaction?>(null),
            nameof(IWalletTransactionRepository.AddTransactionAsync) => Task.CompletedTask,
            _ => Default(method.ReturnType)
        });
        var ledgers = Proxy<IWalletLedgerEntryRepository>((method, _) => method.Name switch
        {
            nameof(IWalletLedgerEntryRepository.AddLedgerEntryAsync) => Task.CompletedTask,
            _ => Default(method.ReturnType)
        });
        var service = new DeliveryExpiryService(CreateUnitOfWork(deliveries, wallets, transactions, ledgers), new RecordingClock(snapshot));

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal((3, 1, 1, 1), (result.ExaminedCount, result.ExpiredCount, result.SkippedCount, result.FailedCount));
        Assert.Equal(DeliveryJobRunStatus.PartiallySucceeded, result.Outcome);
        Assert.Equal(DeliveryStatus.Expired, successfulDelivery.Status);
        Assert.NotNull(result.FailureSummary);
        foreach (var forbidden in new[] { "stack", "Server=", "Password", "storage-key", "signed-url", "wallet=50", "delivery:release:" })
        {
            Assert.DoesNotContain(forbidden, result.FailureSummary!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RunAsync_ClassifiesAConcurrentCompletedReleaseAsReplayNoOp()
    {
        var (result, transactionExecutions, transactionDepth) = await RunConcurrentReleaseReplayAsync();

        Assert.Equal((1, 0, 1, 0), (result.ExaminedCount, result.ExpiredCount, result.SkippedCount, result.FailedCount));
        Assert.Equal(DeliveryJobRunStatus.Succeeded, result.Outcome);
        Assert.Equal(2, transactionExecutions);
        Assert.Equal(0, transactionDepth);
    }

    [Fact]
    public async Task RunAsync_DoesNotClassifyCorruptReleaseLedgerEvidenceAsReplayNoOp()
    {
        var (result, _, _) = await RunConcurrentReleaseReplayAsync(corruptLedgerCurrency: true);

        Assert.Equal((1, 0, 0, 1), (result.ExaminedCount, result.ExpiredCount, result.SkippedCount, result.FailedCount));
        Assert.Equal(DeliveryJobRunStatus.Failed, result.Outcome);
    }

    [Fact]
    public async Task RunAsync_DoesNotClassifyAnActiveDeliveryAsCompletedReleaseReplay()
    {
        var (result, _, _) = await RunConcurrentReleaseReplayAsync(corruptDeliveryState: true);

        Assert.Equal((1, 0, 0, 1), (result.ExaminedCount, result.ExpiredCount, result.SkippedCount, result.FailedCount));
        Assert.Equal(DeliveryJobRunStatus.Failed, result.Outcome);
    }

    private static async Task<(DeliveryJobResultDto Result, int TransactionExecutions, int TransactionDepth)> RunConcurrentReleaseReplayAsync(
        bool corruptLedgerCurrency = false,
        bool corruptDeliveryState = false)
    {
        var snapshot = Snapshot();
        var delivery = ActiveDelivery("delivery");
        var wallet = new Wallet { Id = "wallet", OwnerType = WalletOwnerType.Company, OwnerId = "company", AvailableBalance = 100m, ReservedBalance = 50m, Currency = "EGP" };
        var releaseKey = DeliveryFinancialOperationKeys.ForRelease(delivery.Id);
        var transaction = new WalletTransaction
        {
            Id = "release-transaction",
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.Release,
            IdempotencyKey = releaseKey,
            Amount = delivery.ReservedAmount,
            RelatedDeliveryId = delivery.Id
        };
        var lockCalls = 0;
        var discoveryCalls = 0;
        var transactionLookups = 0;
        var transactionDepth = 0;
        var transactionExecutions = 0;
        var deliveries = Proxy<IDeliveryRepository>((method, _) => method.Name switch
        {
            nameof(IDeliveryRepository.ListOverduePageAsync) => Task.FromResult<IReadOnlyList<OverdueDeliveryReadModel>>(
                discoveryCalls++ == 0 ? [new OverdueDeliveryReadModel(delivery.Id, delivery.DeliveryDateEgypt, snapshot.UtcNow.AddDays(-1))] : []),
            nameof(IDeliveryRepository.FindActiveReservedForUpdateAsync) => FindLockedDelivery(),
            nameof(IDeliveryRepository.FindReservationReplayAsync) => Task.FromResult<DeliveryReservationReplayReadModel?>(new(
                delivery.Id,
                delivery.DoctorId,
                delivery.CampaignId,
                delivery.CompanyId,
                delivery.DeliveryDateEgypt,
                corruptDeliveryState ? DeliveryStatus.Active : DeliveryStatus.Expired,
                corruptDeliveryState ? ReservationStatus.Reserved : ReservationStatus.Released,
                delivery.ReservedAmount)),
            _ => Default(method.ReturnType)
        });
        var wallets = Proxy<IWalletRepository>((method, _) => method.Name switch
        {
            nameof(IWalletRepository.FindActiveWalletForUpdateByOwnerAsync) => Task.FromResult<Wallet?>(wallet),
            nameof(IWalletRepository.StageAvailableBalanceChangeAsync) => Task.CompletedTask,
            nameof(IWalletRepository.StageReservedBalanceChangeAsync) => Task.CompletedTask,
            _ => Default(method.ReturnType)
        });
        var transactions = Proxy<IWalletTransactionRepository>((method, _) => method.Name switch
        {
            nameof(IWalletTransactionRepository.FindTransactionByIdempotencyAsync) => FindReleaseTransaction(),
            nameof(IWalletTransactionRepository.AddTransactionAsync) => throw new InvalidOperationException("simulated unique conflict"),
            _ => Default(method.ReturnType)
        });
        var entries = new[]
        {
            ReleaseEntry(transaction, delivery, WalletBalanceType.Reserved, WalletLedgerEntryDirection.Debit),
            ReleaseEntry(transaction, delivery, WalletBalanceType.Available, WalletLedgerEntryDirection.Credit)
        };
        if (corruptLedgerCurrency)
        {
            entries[0].Currency = "USD";
        }
        var ledgers = Proxy<IWalletLedgerEntryRepository>((method, _) => method.Name switch
        {
            nameof(IWalletLedgerEntryRepository.ListLedgerEntriesByWalletTransactionAsync) => FindReleaseEntries(),
            nameof(IWalletLedgerEntryRepository.AddLedgerEntryAsync) => Task.CompletedTask,
            _ => Default(method.ReturnType)
        });
        var service = new DeliveryExpiryService(
            CreateUnitOfWork(
                deliveries,
                wallets,
                transactions,
                ledgers,
                () =>
                {
                    transactionExecutions++;
                    transactionDepth++;
                },
                () => transactionDepth--),
            new RecordingClock(snapshot));

        var result = await service.RunAsync(CancellationToken.None);

        return (result, transactionExecutions, transactionDepth);

        Task<DoctorAdDelivery?> FindLockedDelivery()
        {
            Assert.True(transactionDepth > 0, "The update-lock delivery read must run inside a Unit of Work transaction.");
            return Task.FromResult<DoctorAdDelivery?>(lockCalls++ == 0 ? delivery : null);
        }

        Task<WalletTransaction?> FindReleaseTransaction()
        {
            Assert.True(transactionDepth > 0, "Release evidence must be read inside the replay transaction.");
            return Task.FromResult<WalletTransaction?>(transactionLookups++ == 0 ? null : transaction);
        }

        Task<IReadOnlyList<WalletLedgerEntry>> FindReleaseEntries()
        {
            Assert.True(transactionDepth > 0, "Ledger evidence must be read inside the replay transaction.");
            return Task.FromResult<IReadOnlyList<WalletLedgerEntry>>(entries);
        }
    }

    [Fact]
    public async Task RunAsync_StopsBetweenCandidatesWhenCancellationIsRequested()
    {
        using var cancellation = new CancellationTokenSource();
        var snapshot = Snapshot();
        var rows = new[]
        {
            new OverdueDeliveryReadModel("first", new DateOnly(2026, 7, 1), snapshot.UtcNow.AddDays(-1)),
            new OverdueDeliveryReadModel("second", new DateOnly(2026, 7, 1), snapshot.UtcNow.AddDays(-1).AddMinutes(1))
        };
        var locks = 0;
        DeliveryJobRunCounters? completedCounters = null;
        var deliveries = Proxy<IDeliveryRepository>((method, _) => method.Name switch
        {
            nameof(IDeliveryRepository.ListOverduePageAsync) => Task.FromResult<IReadOnlyList<OverdueDeliveryReadModel>>(rows),
            nameof(IDeliveryRepository.FindActiveReservedForUpdateAsync) => OnLock(),
            _ => Default(method.ReturnType)
        });
        var jobRuns = Proxy<IDeliveryJobRunRepository>((method, arguments) => method.Name switch
        {
            nameof(IDeliveryJobRunRepository.InterruptStaleRunningAsync) => Task.FromResult(0),
            nameof(IDeliveryJobRunRepository.AddRunningAsync) => Task.CompletedTask,
            nameof(IDeliveryJobRunRepository.CompleteAsync) => CaptureCompletion(arguments),
            _ => Default(method.ReturnType)
        });
        var recoveryDispatches = Proxy<IDeliveryRecoveryDispatchRepository>((method, _) => method.Name switch
        {
            nameof(IDeliveryRecoveryDispatchRepository.CompleteEnqueuedForJobAsync) => Task.FromResult(true),
            _ => Default(method.ReturnType)
        });
        var unitOfWork = CreateUnitOfWork(
            deliveries,
            jobRuns: jobRuns,
            recoveryDispatches: recoveryDispatches);
        var clock = new RecordingClock(snapshot);
        var tracker = new DeliveryJobRunTracker(unitOfWork, NullLogger<DeliveryJobRunTracker>.Instance);
        var service = new DeliveryExpiryService(unitOfWork, clock, tracker, NullLogger<DeliveryExpiryService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(cancellation.Token));
        Assert.Equal(1, locks);
        Assert.NotNull(completedCounters);
        Assert.Equal((1, 0, 1, 0),
            (completedCounters!.ExaminedCount, completedCounters.ExpiredCount, completedCounters.SkippedCount, completedCounters.FailedCount));
        Assert.Equal(1, clock.CaptureCount);

        Task<DoctorAdDelivery?> OnLock()
        {
            locks++;
            cancellation.Cancel();
            return Task.FromResult<DoctorAdDelivery?>(null);
        }

        Task<bool> CaptureCompletion(object?[]? arguments)
        {
            completedCounters = Assert.IsType<DeliveryJobRunCounters>(arguments![2]);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task DailyInjector_InterruptionPersistsAccumulatedProgressFromOneCapturedSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var snapshot = Snapshot();
        DeliveryJobRunCounters? completedCounters = null;
        var candidates = new[]
        {
            new DeliveryQueueCandidateReadModel("first", "doctor", "campaign-first", snapshot.UtcNow.AddDays(-2)),
            new DeliveryQueueCandidateReadModel("second", "doctor", "campaign-second", snapshot.UtcNow.AddDays(-1))
        };
        var messageQueues = Proxy<IMessageQueueRepository>((method, _) => method.Name switch
        {
            nameof(IMessageQueueRepository.ListQueuedDoctorsPageAsync) => Task.FromResult<IReadOnlyList<QueuedDoctorReadModel>>([new("doctor")]),
            nameof(IMessageQueueRepository.ListQueuedCandidatesPageAsync) => Task.FromResult<IReadOnlyList<DeliveryQueueCandidateReadModel>>(candidates),
            nameof(IMessageQueueRepository.FindQueuedItemForUpdateAsync) => Task.FromResult<DoctorMessageQueue?>(null),
            nameof(IMessageQueueRepository.FindQueueItemAsync) => Task.FromResult<DoctorMessageQueue?>(null),
            _ => Default(method.ReturnType)
        });
        var profiles = Proxy<IProfileRepository>((method, _) => method.Name switch
        {
            nameof(IProfileRepository.FindDoctorDeliveryEligibilityForUpdateAsync) => CancelDuringFirstCandidate(),
            _ => Default(method.ReturnType)
        });
        var jobRuns = Proxy<IDeliveryJobRunRepository>((method, arguments) => method.Name switch
        {
            nameof(IDeliveryJobRunRepository.InterruptStaleRunningAsync) => Task.FromResult(0),
            nameof(IDeliveryJobRunRepository.AddRunningAsync) => Task.CompletedTask,
            nameof(IDeliveryJobRunRepository.CompleteAsync) => CaptureCompletion(arguments),
            _ => Default(method.ReturnType)
        });
        var recoveryDispatches = Proxy<IDeliveryRecoveryDispatchRepository>((method, _) => method.Name switch
        {
            nameof(IDeliveryRecoveryDispatchRepository.CompleteEnqueuedForJobAsync) => Task.FromResult(true),
            _ => Default(method.ReturnType)
        });
        var unitOfWork = CreateUnitOfWork(
            Proxy<IDeliveryRepository>(),
            jobRuns: jobRuns,
            recoveryDispatches: recoveryDispatches,
            messageQueues: messageQueues,
            profiles: profiles);
        var clock = new RecordingClock(snapshot);
        var tracker = new DeliveryJobRunTracker(unitOfWork, NullLogger<DeliveryJobRunTracker>.Instance);
        var service = new DailyDeliveryInjectorService(
            unitOfWork,
            clock,
            new DeliverySettlementSnapshotCalculator(),
            new DeliveryCandidateEligibilityPolicy(),
            100,
            tracker,
            NullLogger<DailyDeliveryInjectorService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(cancellation.Token));

        Assert.NotNull(completedCounters);
        Assert.Equal((1, 0, 0, 1),
            (completedCounters!.ExaminedCount, completedCounters.ActivatedCount, completedCounters.SkippedCount, completedCounters.FailedCount));
        Assert.Equal(1, clock.CaptureCount);

        Task<LockedDoctorDeliveryEligibilityReadModel?> CancelDuringFirstCandidate()
        {
            cancellation.Cancel();
            return Task.FromResult<LockedDoctorDeliveryEligibilityReadModel?>(null);
        }

        Task<bool> CaptureCompletion(object?[]? arguments)
        {
            completedCounters = Assert.IsType<DeliveryJobRunCounters>(arguments![2]);
            return Task.FromResult(true);
        }
    }

    private static DoctorAdDelivery ActiveDelivery(string id) => new()
    {
        Id = id,
        DoctorId = "doctor",
        CampaignId = "campaign",
        CompanyId = "company",
        DeliveryDateEgypt = new DateOnly(2026, 7, 1),
        DeliveredAtUtc = Snapshot().UtcNow.AddDays(-1),
        Status = DeliveryStatus.Active,
        ReservationStatus = ReservationStatus.Reserved,
        PricePerMessageSnapshot = 50m,
        PlatformFeePercentSnapshot = 20m,
        PlatformFeeAmount = 10m,
        DoctorEarnings = 40m,
        ReservedAmount = 50m
    };

    private static WalletLedgerEntry ReleaseEntry(
        WalletTransaction transaction,
        DoctorAdDelivery delivery,
        WalletBalanceType balanceType,
        WalletLedgerEntryDirection direction) => new()
    {
        WalletTransactionId = transaction.Id,
        WalletId = transaction.WalletId,
        BalanceType = balanceType,
        Direction = direction,
        Amount = delivery.ReservedAmount,
        MessageDeliveryId = delivery.Id,
        CampaignId = delivery.CampaignId,
        CompanyId = delivery.CompanyId,
        Currency = "EGP",
        IdempotencyKey = transaction.IdempotencyKey
    };

    private static EgyptBusinessTimeSnapshot Snapshot() => new(
        new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc),
        new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.FromHours(3)),
        new DateOnly(2026, 7, 2));

    private static IDomainUnitOfWork CreateUnitOfWork(
        IDeliveryRepository deliveries,
        IWalletRepository? wallets = null,
        IWalletTransactionRepository? transactions = null,
        IWalletLedgerEntryRepository? ledgers = null,
        Action? onTransactionStarted = null,
        Action? onTransactionCompleted = null,
        IDeliveryJobRunRepository? jobRuns = null,
        IDeliveryRecoveryDispatchRepository? recoveryDispatches = null,
        IMessageQueueRepository? messageQueues = null,
        IProfileRepository? profiles = null)
    {
        return Proxy<IDomainUnitOfWork>((method, arguments) => method.Name switch
        {
            "get_Deliveries" => deliveries,
            "get_Wallets" => wallets ?? Proxy<IWalletRepository>(),
            "get_WalletTransactions" => transactions ?? Proxy<IWalletTransactionRepository>(),
            "get_WalletLedgerEntries" => ledgers ?? Proxy<IWalletLedgerEntryRepository>(),
            "get_DeliveryJobRuns" => jobRuns ?? Proxy<IDeliveryJobRunRepository>(),
            "get_DeliveryRecoveryDispatches" => recoveryDispatches ?? Proxy<IDeliveryRecoveryDispatchRepository>(),
            "get_MessageQueues" => messageQueues ?? Proxy<IMessageQueueRepository>(),
            "get_Profiles" => profiles ?? Proxy<IProfileRepository>(),
            nameof(IDomainUnitOfWork.ExecuteInTransactionAsync) or nameof(IDomainUnitOfWork.ExecuteIsolatedInTransactionAsync) => ExecuteObservedTransaction(
                method,
                (Delegate)arguments![0]!,
                (CancellationToken)arguments[1]!,
                onTransactionStarted,
                onTransactionCompleted),
            nameof(IDomainUnitOfWork.SaveChangesAsync) => Task.FromResult(0),
            _ when method.Name.StartsWith("get_", StringComparison.Ordinal) => CreateInterfaceDefault(method.ReturnType),
            _ => Default(method.ReturnType)
        });
    }

    private static object ExecuteObservedTransaction(
        MethodInfo method,
        Delegate operation,
        CancellationToken cancellationToken,
        Action? onStarted,
        Action? onCompleted)
    {
        if (!method.IsGenericMethod)
        {
            return ExecuteObservedTransactionAsync(
                (Func<CancellationToken, Task>)operation,
                cancellationToken,
                onStarted,
                onCompleted);
        }

        return typeof(DeliveryExpiryServiceTests)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == nameof(ExecuteObservedTransactionAsync) && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(method.GetGenericArguments()[0])
            .Invoke(null, [operation, cancellationToken, onStarted, onCompleted])!;
    }

    private static async Task ExecuteObservedTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        Action? onStarted,
        Action? onCompleted)
    {
        onStarted?.Invoke();
        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            onCompleted?.Invoke();
        }
    }

    private static async Task<T> ExecuteObservedTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        Action? onStarted,
        Action? onCompleted)
    {
        onStarted?.Invoke();
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            onCompleted?.Invoke();
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?>? handler = null) where T : class
    {
        var proxy = DispatchProxy.Create<T, RoutingProxy>();
        ((RoutingProxy)(object)proxy).Handler = handler ?? ((method, _) => Default(method.ReturnType));
        return proxy;
    }

    private static object? CreateInterfaceDefault(Type type)
    {
        var method = typeof(DeliveryExpiryServiceTests).GetMethod(nameof(Proxy), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type);
        return method.Invoke(null, [null]);
    }

    private static object? Default(Type type)
    {
        if (type == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var valueType = type.GetGenericArguments()[0];
            var value = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(valueType).Invoke(null, [value]);
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private class RoutingProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private sealed class RecordingClock : IEgyptBusinessClock
    {
        private readonly EgyptBusinessTimeSnapshot snapshot;

        public RecordingClock(EgyptBusinessTimeSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public int CaptureCount { get; private set; }
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public EgyptBusinessTimeSnapshot Capture()
        {
            CaptureCount++;
            return snapshot;
        }
    }
}
