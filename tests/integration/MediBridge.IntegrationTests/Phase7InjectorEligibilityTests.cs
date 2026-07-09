using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7InjectorEligibilityTests
{
    [Fact]
    public async Task Injector_CancelsTerminal_PreservesTemporary_AndBlockedRowsDoNotConsumeCapacity()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 2);
        var terminal = await scenario.AddCandidateAsync("terminal", scenario.UtcNow.AddDays(-4), scenario.UtcNow, campaignStatus: CampaignStatus.Cancelled);
        var temporary = await scenario.AddCandidateAsync("temporary", scenario.UtcNow.AddDays(-3), scenario.UtcNow, campaignStatus: CampaignStatus.Paused);
        var insufficient = await scenario.AddCandidateAsync("insufficient", scenario.UtcNow.AddDays(-2), scenario.UtcNow, availableBalance: 49.99m);
        var funded = await scenario.AddCandidateAsync("funded", scenario.UtcNow.AddDays(-1), scenario.UtcNow, availableBalance: 50m);

        var result = await scenario.RunAsync();

        Assert.Equal((4, 1, 1, 2, 0), (result.ExaminedCount, result.ActivatedCount, result.CancelledCount, result.SkippedCount, result.FailedCount));
        await scenario.WithContextAsync(async context =>
        {
            var rows = await context.DoctorMessageQueues.AsNoTracking().ToDictionaryAsync(item => item.Id);
            Assert.Equal(QueueItemStatus.Cancelled, rows[terminal.QueueId].Status);
            Assert.Equal(QueueItemStatus.Queued, rows[temporary.QueueId].Status);
            Assert.Equal(QueueItemStatus.Queued, rows[insufficient.QueueId].Status);
            Assert.Equal(QueueItemStatus.Activated, rows[funded.QueueId].Status);
            var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == funded.WalletId);
            Assert.Equal((0m, 50m), (wallet.AvailableBalance, wallet.ReservedBalance));
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("non-egp")]
    public async Task Injector_MissingDeletedOrNonEgpWalletLeavesCandidateQueuedAsIsolatedFailure(string walletCase)
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync(
            "wallet",
            scenario.UtcNow.AddDays(-1),
            scenario.UtcNow,
            seedWallet: walletCase != "missing",
            currency: walletCase == "non-egp" ? "USD" : "EGP");
        if (walletCase == "deleted")
        {
            await scenario.WithContextAsync(async context =>
            {
                var wallet = await context.Wallets.SingleAsync(item => item.Id == candidate.WalletId);
                wallet.IsDeleted = true;
                wallet.DeletedAtUtc = scenario.UtcNow;
                await context.SaveChangesAsync();
            });
        }

        var result = await scenario.RunAsync();

        Assert.Equal(1, result.FailedCount);
        await scenario.WithContextAsync(async context =>
            Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.AsNoTracking().SingleAsync(item => item.Id == candidate.QueueId)).Status));
    }
}
