using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7InjectorFinancialTests
{
    [Fact]
    public async Task Injector_AtomicallyCreatesCurrentPriceReserveEvidenceWithoutChargeOrEarn()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync("financial", scenario.UtcNow.AddDays(-1), scenario.UtcNow, availableBalance: 50m);
        await scenario.WithContextAsync(async context =>
        {
            await context.CampaignTargets.AddAsync(new CampaignTarget
            {
                Id = $"target-{Guid.NewGuid():N}",
                CampaignId = candidate.CampaignId,
                DoctorId = scenario.DoctorId,
                SpecializationSnapshot = "Cardiology",
                LocationSnapshot = "Cairo",
                ActivityScoreSnapshot = 95m,
                PricePerMessageSnapshot = 5m,
                CreatedAtUtc = scenario.UtcNow.AddDays(-1)
            });
            await context.SaveChangesAsync();
        });

        var first = await scenario.RunAsync();
        var replay = await scenario.RunAsync();

        Assert.Equal(1, first.ActivatedCount);
        Assert.Equal(0, replay.ActivatedCount);
        await scenario.WithContextAsync(async context =>
        {
            var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync(item => item.CampaignId == candidate.CampaignId);
            Assert.Equal((50m, 20m, 10m, 40m, 50m), (delivery.PricePerMessageSnapshot, delivery.PlatformFeePercentSnapshot, delivery.PlatformFeeAmount, delivery.DoctorEarnings, delivery.ReservedAmount));
            Assert.Equal(scenario.UtcNow, delivery.DeliveredAtUtc);
            var transaction = await context.WalletTransactions.AsNoTracking().SingleAsync();
            Assert.Equal(WalletTransactionType.Reserve, transaction.OperationType);
            Assert.Equal($"delivery:reserve:{delivery.Id}", transaction.IdempotencyKey);
            Assert.Equal(delivery.Id, transaction.RelatedDeliveryId);
            var entries = await context.WalletLedgerEntries.AsNoTracking().OrderBy(item => item.BalanceType).ToArrayAsync();
            Assert.Equal(2, entries.Length);
            Assert.Contains(entries, item => item.BalanceType == WalletBalanceType.Available && item.Direction == WalletLedgerEntryDirection.Debit);
            Assert.Contains(entries, item => item.BalanceType == WalletBalanceType.Reserved && item.Direction == WalletLedgerEntryDirection.Credit);
            Assert.DoesNotContain(await context.WalletTransactions.AsNoTracking().ToArrayAsync(), item => item.OperationType is WalletTransactionType.Charge or WalletTransactionType.Earn);
            Assert.Empty(await context.Wallets.AsNoTracking().Where(item => item.OwnerType == WalletOwnerType.Doctor).ToArrayAsync());
        });
    }

    [Fact]
    public async Task Injector_ForcedLedgerFailureRollsBackEveryCandidateMutation()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync("rollback", scenario.UtcNow.AddDays(-1), scenario.UtcNow, availableBalance: 50m);
        await scenario.WithContextAsync(context => context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [TR_Phase7_FailInjectorLedger] ON [WalletLedgerEntries] AFTER INSERT AS
            BEGIN THROW 51010, 'forced injector ledger failure', 1; END
            """));

        var result = await scenario.RunAsync();

        Assert.Equal(1, result.FailedCount);
        await scenario.WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Phase7_FailInjectorLedger]");
            Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.AsNoTracking().SingleAsync(item => item.Id == candidate.QueueId)).Status);
            Assert.Empty(await context.DoctorAdDeliveries.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.WalletTransactions.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.WalletLedgerEntries.AsNoTracking().ToArrayAsync());
            var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == candidate.WalletId);
            Assert.Equal((50m, 0m), (wallet.AvailableBalance, wallet.ReservedBalance));
        });
    }

    [Fact]
    public async Task Injector_ExistingDeliveryWithoutCompleteReserveEvidenceIsFailedNotReplayed()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync("incomplete-replay", scenario.UtcNow.AddDays(-1), scenario.UtcNow);
        await Phase7DeliveryTestHelpers.SeedDeliveryAsync(
            scenario.Services,
            $"legacy-delivery-{Guid.NewGuid():N}",
            scenario.DoctorId,
            candidate.CampaignId,
            candidate.CompanyId,
            scenario.BusinessDateEgypt,
            scenario.UtcNow,
            DeliveryStatus.Active,
            ReservationStatus.Reserved,
            50m,
            20m,
            10m,
            40m,
            50m,
            scenario.UtcNow);

        var result = await scenario.RunAsync();

        Assert.Equal((1, 0, 0, 0, 1),
            (result.ExaminedCount, result.ActivatedCount, result.CancelledCount, result.SkippedCount, result.FailedCount));
        await scenario.WithContextAsync(async context =>
            Assert.Equal(QueueItemStatus.Queued, (await context.DoctorMessageQueues.FindAsync(candidate.QueueId))!.Status));
    }

    [Fact]
    public async Task Injector_OrphanReserveKeyWithoutDeliveryIsFailedNotReplayed()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync("orphan-reserve", scenario.UtcNow.AddDays(-1), scenario.UtcNow);
        var deliveryId = DeriveDeliveryId(scenario.BusinessDateEgypt, candidate.QueueId);
        await scenario.WithContextAsync(async context =>
        {
            await context.WalletTransactions.AddAsync(new WalletTransaction
            {
                Id = $"orphan-transaction-{Guid.NewGuid():N}",
                WalletId = candidate.WalletId,
                OperationType = WalletTransactionType.Reserve,
                IdempotencyKey = DeliveryFinancialOperationKeys.ForReserve(deliveryId),
                Amount = 50m,
                RelatedDeliveryId = null,
                CreatedAtUtc = scenario.UtcNow
            });
            await context.SaveChangesAsync();
        });

        var result = await scenario.RunAsync();

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public async Task ConcurrentInjectorsRespectSameDoctorLimitAndSameCompanyBalance()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 1);
        await scenario.AddCandidateAsync("concurrent-a", scenario.UtcNow.AddDays(-2), scenario.UtcNow, availableBalance: 50m);
        await scenario.AddCandidateAsync("concurrent-b", scenario.UtcNow.AddDays(-1), scenario.UtcNow, availableBalance: 50m);

        await Task.WhenAll(scenario.RunAsync(), scenario.RunAsync());

        await scenario.WithContextAsync(async context =>
        {
            Assert.Equal(1, await context.DoctorAdDeliveries.CountAsync(item => item.DoctorId == scenario.DoctorId));
            Assert.Equal(1, await context.WalletTransactions.CountAsync(item => item.OperationType == WalletTransactionType.Reserve));
            Assert.Equal(2, await context.WalletLedgerEntries.CountAsync());
        });
    }

    [Fact]
    public async Task ConcurrentInjectorsCannotOverspendOneCompanyWalletAcrossCampaigns()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync(dailyLimit: 1);
        var first = await scenario.AddCandidateAsync("shared-company-a", scenario.UtcNow.AddDays(-2), scenario.UtcNow, availableBalance: 50m);
        var secondDoctor = await scenario.AddApprovedDoctorAsync("shared-company-b");
        var secondCampaignId = $"campaign-shared-company-b-{Guid.NewGuid():N}";
        var secondQueueId = $"queue-shared-company-b-{Guid.NewGuid():N}";
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            scenario.Services,
            secondCampaignId,
            first.CompanyId,
            CampaignStatus.Approved,
            scenario.UtcNow.AddDays(-1),
            scenario.UtcNow.AddDays(-1),
            false,
            null);
        await Phase7DeliveryTestHelpers.SeedFifoQueueRowAsync(
            scenario.Services,
            secondQueueId,
            secondCampaignId,
            secondDoctor.DoctorId,
            scenario.UtcNow.AddDays(-1),
            scenario.UtcNow,
            scenario.UtcNow);

        await Task.WhenAll(scenario.RunAsync(), scenario.RunAsync());

        await scenario.WithContextAsync(async context =>
        {
            Assert.Equal(1, await context.DoctorAdDeliveries.CountAsync(item => item.CompanyId == first.CompanyId));
            var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == first.WalletId);
            Assert.Equal((0m, 50m), (wallet.AvailableBalance, wallet.ReservedBalance));
            Assert.Equal(1, await context.DoctorMessageQueues.CountAsync(item => item.Status == QueueItemStatus.Queued));
        });
    }

    private static string DeriveDeliveryId(DateOnly businessDateEgypt, string queueItemId)
    {
        var source = System.Text.Encoding.UTF8.GetBytes($"{businessDateEgypt:yyyy-MM-dd}|{queueItemId}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)).ToLowerInvariant();
    }
}
