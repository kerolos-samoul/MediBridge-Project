using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7InjectorFifoAndLimitTests
{
    [Fact]
    public async Task Injector_UsesSubmissionThenIdFifo_IgnoresQueuedAt_AndStopsAtGlobalLimit()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 2);
        var late = await scenario.AddCandidateAsync("late", scenario.UtcNow.AddDays(-1), scenario.UtcNow.AddDays(-4));
        var tieB = await scenario.AddCandidateAsync("tie-b", scenario.UtcNow.AddDays(-2), scenario.UtcNow.AddDays(-5));
        var tieA = await scenario.AddCandidateAsync("tie-a", scenario.UtcNow.AddDays(-2), scenario.UtcNow.AddDays(-1));
        var expected = new[] { tieA, tieB }.OrderBy(item => item.QueueId, StringComparer.Ordinal).Select(item => item.QueueId).ToArray();

        var result = await scenario.RunAsync(batchSize: 1);

        Assert.Equal(2, result.ActivatedCount);
        await scenario.WithContextAsync(async context =>
        {
            var activated = await context.DoctorMessageQueues.AsNoTracking()
                .Where(item => item.Status == QueueItemStatus.Activated)
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync();
            Assert.Equal(expected, activated);
            Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.FindAsync(late.QueueId))!.Status);
            Assert.Equal(2, await context.DoctorAdDeliveries.CountAsync(item => item.DoctorId == scenario.DoctorId));
        });
    }

    [Theory]
    [InlineData(DeliveryStatus.Active)]
    [InlineData(DeliveryStatus.Accepted)]
    [InlineData(DeliveryStatus.Rejected)]
    public async Task Injector_CountsEveryExistingCurrentDayDeliveryStatusAndNeverDeletesIt(DeliveryStatus status)
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 1);
        await scenario.SeedExistingDeliveryAsync(status.ToString(), status);
        var candidate = await scenario.AddCandidateAsync("new", scenario.UtcNow.AddDays(-1), scenario.UtcNow.AddDays(-1));

        var result = await scenario.RunAsync();

        Assert.Equal(0, result.ActivatedCount);
        await scenario.WithContextAsync(async context =>
        {
            Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.FindAsync(candidate.QueueId))!.Status);
            Assert.Equal(1, await context.DoctorAdDeliveries.CountAsync(item => item.DoctorId == scenario.DoctorId));
        });
    }

    [Fact]
    public async Task Injector_ZeroOrReducedLimitAddsNothingAndPreservesExistingDeliveries()
    {
        await using (var zeroLimit = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 0))
        {
            var queued = await zeroLimit.AddCandidateAsync("zero-limit", zeroLimit.UtcNow.AddDays(-1), zeroLimit.UtcNow);
            var result = await zeroLimit.RunAsync();
            Assert.Equal(0, result.ActivatedCount);
            await zeroLimit.WithContextAsync(async context =>
                Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.FindAsync(queued.QueueId))!.Status));
        }

        await using var reduced = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 1);
        await reduced.SeedExistingDeliveryAsync("reduced-active", DeliveryStatus.Active);
        await reduced.SeedExistingDeliveryAsync("reduced-accepted", DeliveryStatus.Accepted);
        await reduced.AddCandidateAsync("reduced-new", reduced.UtcNow.AddDays(-1), reduced.UtcNow);
        var reducedResult = await reduced.RunAsync();
        Assert.Equal(0, reducedResult.ActivatedCount);
        await reduced.WithContextAsync(async context =>
            Assert.Equal(2, await context.DoctorAdDeliveries.CountAsync(item => item.DoctorId == reduced.DoctorId)));
    }

    [Fact]
    public async Task QueueRepository_KeysetContinuesEqualTimestampsWithoutDuplicates()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 5);
        var instant = scenario.UtcNow.AddDays(-2);
        var candidates = new[]
        {
            await scenario.AddCandidateAsync("one", instant, scenario.UtcNow.AddDays(-1)),
            await scenario.AddCandidateAsync("two", instant, scenario.UtcNow.AddDays(-3)),
            await scenario.AddCandidateAsync("three", instant.AddHours(1), scenario.UtcNow.AddDays(-4))
        };
        using var scope = scenario.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMessageQueueRepository>();
        var seen = new List<string>();
        QueuedCandidateCursor? cursor = null;
        while (true)
        {
            var page = await repository.ListQueuedCandidatesPageAsync(scenario.DoctorId, cursor, 1);
            if (page.Count == 0) break;
            seen.Add(page[0].Id);
            cursor = new QueuedCandidateCursor(page[0].CampaignSubmittedAtUtc!.Value, page[0].Id);
        }

        var expected = candidates.Take(2)
            .Select(item => item.QueueId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Append(candidates[2].QueueId);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task Injector_ClassifiesMalformedNullSubmissionAsIsolatedFailure()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var malformed = await scenario.AddCandidateAsync("malformed", scenario.UtcNow.AddDays(-1), scenario.UtcNow.AddDays(-1));
        await scenario.WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE [DoctorMessageQueues] NOCHECK CONSTRAINT [CK_DoctorMessageQueues_QueuedCampaignSubmittedAtUtc]");
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [DoctorMessageQueues] SET [CampaignSubmittedAtUtc] = NULL WHERE [Id] = {malformed.QueueId}");
        });

        var result = await scenario.RunAsync();

        Assert.Equal((1, 0, 0, 0, 1), (result.ExaminedCount, result.ActivatedCount, result.CancelledCount, result.SkippedCount, result.FailedCount));
    }
}
