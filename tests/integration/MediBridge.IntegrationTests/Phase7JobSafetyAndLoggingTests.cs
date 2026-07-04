using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Hangfire;
using Hangfire.States;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7JobSafetyAndLoggingTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ApiAndServiceLogs_ContainNoRawDiagnosticsSecretsLocationsBalancesOrOperationKeys()
    {
        var roots = new[] { "MediBridge.APIs", "MediBridge.Services" };
        var forbiddenLogMarkers = new[]
        {
            "LogError(ex,", "LogError(exception,", "{StorageKey}", "{PublicId}", "{AccessUrl}",
            "{SignedUrl}", "{SecureUrl", "{ConnectionString}", "{Password", "{Username}",
            "{ConfigCloudName}", "{UrlCloudName}", "{CloudinaryUrl", "{FolderPrefix}",
            "{AvailableBalance}", "{ReservedBalance}", "{WalletBalance}", "{IdempotencyKey}",
            "{Arguments}", "InvocationData"
        };

        var unsafeCalls = roots
            .Select(root => Path.Combine(RepositoryRoot, root))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => Regex.Matches(
                    File.ReadAllText(file),
                    @"Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\([\s\S]*?\);",
                    RegexOptions.CultureInvariant)
                .Select(match => new { file, call = match.Value }))
            .Where(item => forbiddenLogMarkers.Any(marker => item.call.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Select(item => $"{Path.GetRelativePath(RepositoryRoot, item.file)}: {item.call.Replace(Environment.NewLine, " ")}")
            .ToArray();

        Assert.True(unsafeCalls.Length == 0, $"Unsafe logging calls:{Environment.NewLine}{string.Join(Environment.NewLine, unsafeCalls)}");
    }

    [Fact]
    public async Task ConcurrentRecoveryAfter0005_CreatesOneClaimPerJobAndOneExpiryFirstChainWithoutBusinessMutation()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var now = new DateTime(2026, 7, 4, 0, 10, 0, DateTimeKind.Utc);
        var enqueuer = new RecordingEnqueuer();

        async Task RecoverAsync()
        {
            using var scope = factory.Services.CreateScope();
            var coordinator = new DeliveryJobRecoveryCoordinator(
                scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>(),
                new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(now))),
                enqueuer,
                new RecordingLogger<DeliveryJobRecoveryCoordinator>());
            await coordinator.RecoverAsync();
        }

        await Task.WhenAll(RecoverAsync(), RecoverAsync(), RecoverAsync());

        Assert.Equal(new[] { "expiry", "continuation:job-expiry" }, enqueuer.Calls.ToArray());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var dispatches = await db.DeliveryRecoveryDispatches.AsNoTracking().OrderBy(item => item.JobType).ToListAsync();
        Assert.Equal(2, dispatches.Count);
        Assert.All(dispatches, item => Assert.Equal(RecoveryDispatchStatus.Enqueued, item.Status));
        Assert.Equal(dispatches[0].Id, dispatches[1].DependsOnDispatchId);
        Assert.Equal(0, await db.DoctorAdDeliveries.CountAsync());
        Assert.Equal(0, await db.DoctorMessageQueues.CountAsync());
        Assert.Equal(0, await db.WalletTransactions.CountAsync());
        Assert.Equal(0, await db.WalletLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task RecoveryBefore0005_EnqueuesOnlyExpiry()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var utc = new DateTime(2026, 7, 3, 21, 4, 0, DateTimeKind.Utc); // 00:04 Cairo during DST.
        var enqueuer = new RecordingEnqueuer();

        using var scope = factory.Services.CreateScope();
        var coordinator = new DeliveryJobRecoveryCoordinator(
            scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>(),
            new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(utc))),
            enqueuer,
            new RecordingLogger<DeliveryJobRecoveryCoordinator>());
        await coordinator.RecoverAsync();

        Assert.Equal(new[] { "expiry" }, enqueuer.Calls.ToArray());
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var dispatch = Assert.Single(await db.DeliveryRecoveryDispatches.AsNoTracking().ToListAsync());
        Assert.Equal(DeliveryJobType.ExpiryCleaner, dispatch.JobType);
    }

    [Fact]
    public async Task SchedulerInterruption_RecordsSafeFailure_AndRestartReconcilesPendingWorkAtLeastOnce()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var now = new DateTime(2026, 7, 4, 0, 10, 0, DateTimeKind.Utc);
        var log = new RecordingLogger<DeliveryJobRecoveryCoordinator>();

        using (var failedScope = factory.Services.CreateScope())
        {
            var failing = new RecordingEnqueuer
            {
                Failure = new InvalidOperationException(
                    "password=secret connectionstring=private storagekey=private signedurl=https://private " +
                    "availablebalance=999 idempotencykey=private invocationdata=private")
            };
            var coordinator = new DeliveryJobRecoveryCoordinator(
                failedScope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>(),
                new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(now))),
                failing,
                log);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RecoverAsync());
        }

        using (var verificationScope = factory.Services.CreateScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var claims = await db.DeliveryRecoveryDispatches.AsNoTracking().ToListAsync();
            Assert.Equal(2, claims.Count);
            Assert.All(claims, claim => Assert.Equal(RecoveryDispatchStatus.Failed, claim.Status));
            Assert.All(claims, claim => Assert.Equal("The delivery scheduler could not acknowledge recovery work.", claim.SafeFailureSummary));
        }

        var recoveryEnqueuer = new RecordingEnqueuer();
        using (var restartScope = factory.Services.CreateScope())
        {
            var coordinator = new DeliveryJobRecoveryCoordinator(
                restartScope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>(),
                new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(now.AddMinutes(1)))),
                recoveryEnqueuer,
                log);
            await coordinator.RecoverAsync();
        }

        Assert.Equal(new[] { "expiry", "continuation:job-expiry" }, recoveryEnqueuer.Calls.ToArray());
        var renderedLogs = string.Join("\n", log.Messages);
        foreach (var sensitiveMarker in new[]
                 {
                     "secret", "connectionstring", "storagekey", "signedurl", "availablebalance",
                     "idempotencykey", "invocationdata"
                 })
        {
            Assert.DoesNotContain(sensitiveMarker, renderedLogs, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CancelledRecovery_DoesNotSwallowCancellationOrRecordSensitiveDiagnostics()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new DeliveryJobRecoveryCoordinator(
            scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>(),
            new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(2026, 7, 4, 0, 10, 0, TimeSpan.Zero))),
            new RecordingEnqueuer(),
            new RecordingLogger<DeliveryJobRecoveryCoordinator>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RecoverAsync(cancellation.Token));
    }

    [Fact]
    public async Task ProtectedStorageRequeue_KeepsThePersistedServiceInterfaceMethodAndDeliveryQueue()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var storage = factory.Services.GetRequiredService<JobStorage>();
        var client = new BackgroundJobClient(storage);
        var enqueuer = new MediBridge.APIs.Extensions.HangfireDeliveryJobEnqueuer(
            client,
            Microsoft.Extensions.Options.Options.Create(new MediBridge.APIs.Config.DeliveryJobOptions()));

        var persisted = await enqueuer.EnqueueExpiryAsync();
        using (var failedConnection = storage.GetConnection())
        {
            using var transaction = failedConnection.CreateWriteTransaction();
            transaction.SetJobState(
                persisted.SchedulerJobId,
                new FailedState(new InvalidOperationException("safe synthetic failure")));
            transaction.Commit();
            Assert.Equal(FailedState.StateName, failedConnection.GetJobData(persisted.SchedulerJobId).State);
        }
        Assert.True(client.ChangeState(
            persisted.SchedulerJobId,
            new EnqueuedState("delivery"),
            FailedState.StateName));

        using var connection = storage.GetConnection();
        var data = connection.GetJobData(persisted.SchedulerJobId);
        Assert.NotNull(data);
        Assert.Equal(typeof(IDeliveryExpiryService), data.Job.Type);
        Assert.Equal(nameof(IDeliveryExpiryService.RunAsync), data.Job.Method.Name);
        Assert.Equal(EnqueuedState.StateName, data.State);
    }

    private sealed class RecordingEnqueuer : IDeliveryJobEnqueuer
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public Exception? Failure { get; init; }

        public Task<DeliveryJobEnqueueResult> EnqueueExpiryAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            Calls.Enqueue("expiry");
            return Task.FromResult(new DeliveryJobEnqueueResult("job-expiry"));
        }

        public Task<DeliveryJobEnqueueResult> EnqueueInjectorContinuationAsync(string expirySchedulerJobId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue($"continuation:{expirySchedulerJobId}");
            return Task.FromResult(new DeliveryJobEnqueueResult("job-injector"));
        }

        public Task<DeliveryJobEnqueueResult> EnqueueInjectorAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue("injector");
            return Task.FromResult(new DeliveryJobEnqueueResult("job-injector"));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Enqueue(formatter(state, null));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;
        public FixedTimeProvider(DateTimeOffset now) => this.now = now;
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediBridge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class WebAppFactory : ConfiguredWebAppFactory;
}
