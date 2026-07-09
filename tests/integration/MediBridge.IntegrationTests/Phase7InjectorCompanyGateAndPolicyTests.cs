using System.Data.Common;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7InjectorCompanyGateAndPolicyTests
{
    [Fact]
    public async Task Injector_BlocksOnlyCompanyWithOverdueDeliveryThenActivatesAfterRelease()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 2);
        var blocked = await scenario.AddCandidateAsync("blocked", scenario.UtcNow.AddDays(-2), scenario.UtcNow, availableBalance: 100m);
        var clear = await scenario.AddCandidateAsync("clear", scenario.UtcNow.AddDays(-1), scenario.UtcNow, availableBalance: 100m);
        await scenario.SeedOverdueDeliveryAsync(blocked);

        var first = await scenario.RunAsync();

        Assert.Equal((1, 1), (first.ActivatedCount, first.SkippedCount));
        await scenario.WithContextAsync(async context =>
        {
            var overdue = await context.DoctorAdDeliveries.SingleAsync(item => item.CompanyId == blocked.CompanyId && item.DeliveryDateEgypt < scenario.BusinessDateEgypt);
            overdue.MarkExpiredAndReleased(scenario.UtcNow);
            var wallet = await context.Wallets.SingleAsync(item => item.Id == blocked.WalletId);
            wallet.ReservedBalance -= 50m;
            wallet.AvailableBalance += 50m;
            await context.SaveChangesAsync();
        });

        var second = await scenario.RunAsync();

        Assert.Equal(1, second.ActivatedCount);
        await scenario.WithContextAsync(async context =>
        {
            Assert.Equal(QueueItemStatus.Activated, (await context.DoctorMessageQueues.FindAsync(blocked.QueueId))!.Status);
            Assert.Equal(QueueItemStatus.Activated, (await context.DoctorMessageQueues.FindAsync(clear.QueueId))!.Status);
        });
    }

    [Fact]
    public async Task Injector_Before0005CairoReturnsDeferredWithoutMutation()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(new DateTime(2026, 7, 2, 21, 3, 0, DateTimeKind.Utc));
        var candidate = await scenario.AddCandidateAsync("early", scenario.UtcNow.AddDays(-1), scenario.UtcNow);

        var result = await scenario.RunAsync();

        Assert.Equal(DeliveryJobRunStatus.Deferred, result.Outcome);
        Assert.Equal(0, result.ExaminedCount);
        await scenario.WithContextAsync(async context =>
        {
            Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.FindAsync(candidate.QueueId))!.Status);
            Assert.Empty(await context.DoctorAdDeliveries.AsNoTracking().ToArrayAsync());
        });
    }

    [Fact]
    public async Task Injector_WaitsForInFlightExpiryTransactionAndUsesItsCommittedOutcome()
    {
        var gateObserver = new GateQueryObserver();
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(commandInterceptor: gateObserver);
        var candidate = await scenario.AddCandidateAsync("in-flight", scenario.UtcNow.AddDays(-1), scenario.UtcNow, availableBalance: 100m);
        await scenario.SeedOverdueDeliveryAsync(candidate);

        using var scope = scenario.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var overdue = await context.DoctorAdDeliveries
            .FromSqlInterpolated($"SELECT * FROM [DoctorAdDeliveries] WITH (UPDLOCK, HOLDLOCK) WHERE [CompanyId] = {candidate.CompanyId} AND [DeliveryDateEgypt] < {scenario.BusinessDateEgypt}")
            .SingleAsync();

        overdue.MarkExpiredAndReleased(scenario.UtcNow);
        var wallet = await context.Wallets.SingleAsync(item => item.Id == candidate.WalletId);
        wallet.ReservedBalance -= 50m;
        wallet.AvailableBalance += 50m;
        await context.SaveChangesAsync();

        var injector = scenario.RunAsync();
        await gateObserver.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(injector.IsCompleted);

        await transaction.CommitAsync();

        var result = await injector;
        Assert.Equal(1, result.ActivatedCount);
    }

    private sealed class GateQueryObserver : DbCommandInterceptor
    {
        public TaskCompletionSource QueryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("READCOMMITTEDLOCK", StringComparison.Ordinal) &&
                command.CommandText.Contains("DoctorAdDeliveries", StringComparison.Ordinal))
            {
                QueryStarted.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("overlapping")]
    [InlineData("out-of-range")]
    [InlineData("rounded-invalid")]
    public async Task Injector_InvalidEffectivePolicyFailsOnlyCandidateWithoutFallback(string policyCase)
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(seedPolicy: false);
        if (policyCase == "overlapping")
        {
            await scenario.AddPolicyAsync("one", 10m, scenario.UtcNow.AddDays(-2), null);
            await scenario.AddPolicyAsync("two", 20m, scenario.UtcNow.AddDays(-1), null);
        }
        else if (policyCase == "out-of-range")
        {
            await scenario.AddPolicyAsync("invalid", 101m, scenario.UtcNow.AddDays(-1), null);
        }
        else if (policyCase == "rounded-invalid")
        {
            await scenario.AddPolicyAsync("rounded", 1m, scenario.UtcNow.AddDays(-1), null);
            await scenario.WithContextAsync(async context =>
            {
                var doctor = await context.DoctorProfiles.SingleAsync(item => item.Id == scenario.DoctorId);
                doctor.PricePerMessage = 0.01m;
                await context.SaveChangesAsync();
            });
        }
        var candidate = await scenario.AddCandidateAsync("policy", scenario.UtcNow.AddDays(-1), scenario.UtcNow);

        var result = await scenario.RunAsync();

        Assert.Equal(1, result.FailedCount);
        await scenario.WithContextAsync(async context =>
        {
            Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.FindAsync(candidate.QueueId))!.Status);
            Assert.Empty(await context.DoctorAdDeliveries.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.WalletTransactions.AsNoTracking().ToArrayAsync());
        });
    }
}
