using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using MediBridge.Services.Interfaces;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7DeliveryJobRunIntegrationTests
{
    [Fact]
    public async Task Tracker_InterruptsStaleRun_AndPersistsAccurateTerminalCounters()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var now = new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc);

        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            db.DeliveryJobRuns.Add(new DeliveryJobRun
            {
                Id = "stale-run",
                JobType = DeliveryJobType.ExpiryCleaner,
                BusinessDateEgypt = new DateOnly(2026, 7, 2),
                Status = DeliveryJobRunStatus.Running,
                StartedAtUtc = now.AddDays(-2),
                CreatedAtUtc = now.AddDays(-2)
            });
            await db.SaveChangesAsync();
        }

        string runId;
        using (var runScope = factory.Services.CreateScope())
        {
            var tracker = runScope.ServiceProvider.GetRequiredService<DeliveryJobRunTracker>();
            var handle = await tracker.StartAsync(
                DeliveryJobType.ExpiryCleaner,
                new DateOnly(2026, 7, 4),
                now,
                CancellationToken.None);
            runId = handle.RunId;
            await tracker.CompleteAsync(
                handle,
                DeliveryJobRunStatus.PartiallySucceeded,
                new DeliveryJobRunCounters(7, 0, 3, 0, 2, 2),
                now.AddMinutes(1),
                "One or more delivery expiry candidates failed.",
                CancellationToken.None);
        }

        using var verificationScope = factory.Services.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var stale = await verification.DeliveryJobRuns.AsNoTracking().SingleAsync(run => run.Id == "stale-run");
        var completed = await verification.DeliveryJobRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
        Assert.Equal(DeliveryJobRunStatus.Interrupted, stale.Status);
        Assert.Equal(now, stale.CompletedAtUtc);
        Assert.Equal(DeliveryJobRunStatus.PartiallySucceeded, completed.Status);
        Assert.Equal((7, 3, 2, 2), (completed.ExaminedCount, completed.ExpiredCount, completed.SkippedCount, completed.FailedCount));
    }

    [Fact]
    public async Task Repository_RedactsAndBoundsFailureSummary_AndFailedRunIsNotCoverage()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var now = new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc);
        const string runId = "failed-run";

        using (var scope = factory.Services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await unitOfWork.DeliveryJobRuns.AddRunningAsync(new DeliveryJobRun
                {
                    Id = runId,
                    JobType = DeliveryJobType.DailyInjector,
                    BusinessDateEgypt = new DateOnly(2026, 7, 4),
                    StartedAtUtc = now,
                    CreatedAtUtc = now
                }, ct);
            });
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                Assert.True(await unitOfWork.DeliveryJobRuns.CompleteAsync(
                    runId,
                    DeliveryJobRunStatus.Failed,
                    new DeliveryJobRunCounters(1, 0, 0, 0, 0, 1),
                    now.AddSeconds(1),
                    "safe prefix\npassword=secret " + new string('x', 3000),
                    ct));
            });
            Assert.False(await unitOfWork.DeliveryJobRuns.HasCurrentDateCoverageAsync(
                DeliveryJobType.DailyInjector, new DateOnly(2026, 7, 4)));
        }

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var run = await db.DeliveryJobRuns.AsNoTracking().SingleAsync(item => item.Id == runId);
        Assert.NotNull(run.SafeFailureSummary);
        Assert.DoesNotContain("secret", run.SafeFailureSummary!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', run.SafeFailureSummary!);
        Assert.True(run.SafeFailureSummary!.Length <= DeliveryJobRun.MaxSafeFailureSummaryLength);
    }

    [Fact]
    public async Task RecoveryDispatch_TransitionsFailedToPending_AndRowVersionRejectsStaleWriter()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var date = new DateOnly(2026, 7, 4);
        var now = new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc);
        string dispatchId;

        using (var scope = factory.Services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
            dispatchId = await unitOfWork.ExecuteIsolatedInTransactionAsync(async ct =>
            {
                var claim = await unitOfWork.DeliveryRecoveryDispatches.FindOrCreateClaimAsync(
                    date, DeliveryJobType.ExpiryCleaner, now, ct);
                return claim.Id;
            });
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                Assert.True(await unitOfWork.DeliveryRecoveryDispatches.CompleteAsync(
                    dispatchId, RecoveryDispatchStatus.Failed, now.AddSeconds(1), "safe failure", ct));
            });
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                Assert.True(await unitOfWork.DeliveryRecoveryDispatches.ResetForRetryAsync(
                    dispatchId, now.AddSeconds(2), ct));
            });
        }

        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var first = await firstDb.DeliveryRecoveryDispatches.SingleAsync(item => item.Id == dispatchId);
        var second = await secondDb.DeliveryRecoveryDispatches.SingleAsync(item => item.Id == dispatchId);
        Assert.Equal(RecoveryDispatchStatus.Pending, first.Status);
        first.SafeFailureSummary = "winner";
        await firstDb.SaveChangesAsync();
        second.SafeFailureSummary = "stale";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentClaimCreation_ProducesOneDateAndJobClaim()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var date = new DateOnly(2026, 7, 4);
        var now = new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc);

        async Task<string> ClaimAsync()
        {
            using var scope = factory.Services.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
            return await unitOfWork.ExecuteIsolatedInTransactionAsync(async ct =>
                (await unitOfWork.DeliveryRecoveryDispatches.FindOrCreateClaimAsync(
                    date, DeliveryJobType.ExpiryCleaner, now, ct)).Id);
        }

        var ids = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ClaimAsync()));
        Assert.Single(ids.Distinct(StringComparer.Ordinal));
        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await db.DeliveryRecoveryDispatches.CountAsync());
    }

    [Fact]
    public async Task ProductionServices_RecordSucceededAndDeferredRuns_AndCompleteObservedRecoveryDispatch()
    {
        var utc = new DateTime(2026, 7, 3, 21, 4, 0, DateTimeKind.Utc); // 00:04 Cairo during DST.
        await using var factory = new FixedClockWebAppFactory(utc);
        await factory.InitializeDatabaseAsync();

        using (var claimScope = factory.Services.CreateScope())
        {
            var unitOfWork = claimScope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
            await unitOfWork.ExecuteIsolatedInTransactionAsync(async ct =>
            {
                var claim = await unitOfWork.DeliveryRecoveryDispatches.FindOrCreateClaimAsync(
                    new DateOnly(2026, 7, 4), DeliveryJobType.ExpiryCleaner, utc, ct);
                Assert.True(await unitOfWork.DeliveryRecoveryDispatches.RecordEnqueuedAsync(
                    claim.Id, "scheduler-expiry", null, utc, ct));
                return true;
            });
        }

        using (var runScope = factory.Services.CreateScope())
        {
            var expiry = await runScope.ServiceProvider.GetRequiredService<IDeliveryExpiryService>().RunAsync();
            var injector = await runScope.ServiceProvider.GetRequiredService<IDailyDeliveryInjectorService>().RunAsync();
            Assert.Equal(DeliveryJobRunStatus.Succeeded, expiry.Outcome);
            Assert.Equal(DeliveryJobRunStatus.Deferred, injector.Outcome);
        }

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var runs = await db.DeliveryJobRuns.AsNoTracking().OrderBy(item => item.JobType).ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.Equal(DeliveryJobRunStatus.Succeeded, runs.Single(item => item.JobType == DeliveryJobType.ExpiryCleaner).Status);
        Assert.Equal(DeliveryJobRunStatus.Deferred, runs.Single(item => item.JobType == DeliveryJobType.DailyInjector).Status);
        Assert.Equal(RecoveryDispatchStatus.Completed,
            (await db.DeliveryRecoveryDispatches.AsNoTracking().SingleAsync()).Status);
        var repository = verificationScope.ServiceProvider.GetRequiredService<IDeliveryJobRunRepository>();
        Assert.False(await repository.HasCurrentDateCoverageAsync(
            DeliveryJobType.DailyInjector, new DateOnly(2026, 7, 4)));
    }

    private sealed class WebAppFactory : ConfiguredWebAppFactory;

    private sealed class FixedClockWebAppFactory(DateTime utcNow) : ConfiguredWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(utcNow)));
            });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;
        public FixedTimeProvider(DateTimeOffset now) => this.now = now;
        public override DateTimeOffset GetUtcNow() => now;
    }
}
