using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7ExpiryFinancialTests
{
    [Fact]
    public async Task RunAsync_ReleasesExactReservationWithOneBalancedReferencedFinancialOperation()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "financial", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));

        var result = await Phase7ExpiryEligibilityTests.RunAsync(factory.Services, now);

        Assert.Equal(1, result.ExpiredCount);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == seed.WalletId);
        Assert.Equal(150m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);

        var deliveryId = seed.DeliveryIds["financial"];
        var transaction = await context.WalletTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(WalletTransactionType.Release, transaction.OperationType);
        Assert.Equal($"delivery:release:{deliveryId}", transaction.IdempotencyKey);
        Assert.Equal(deliveryId, transaction.RelatedDeliveryId);
        Assert.Equal(50m, transaction.Amount);

        var entries = await context.WalletLedgerEntries.AsNoTracking().OrderBy(item => item.BalanceType).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, item => item.BalanceType == WalletBalanceType.Reserved && item.Direction == WalletLedgerEntryDirection.Debit && item.Amount == 50m);
        Assert.Contains(entries, item => item.BalanceType == WalletBalanceType.Available && item.Direction == WalletLedgerEntryDirection.Credit && item.Amount == 50m);
        Assert.All(entries, item =>
        {
            Assert.Equal(deliveryId, item.MessageDeliveryId);
            Assert.Equal(seed.CompanyId, item.CompanyId);
            Assert.False(string.IsNullOrWhiteSpace(item.CampaignId));
            Assert.Null(item.DoctorId);
            Assert.Equal(transaction.IdempotencyKey, item.IdempotencyKey);
        });
        Assert.DoesNotContain(await context.WalletTransactions.AsNoTracking().ToListAsync(), item => item.OperationType is WalletTransactionType.Charge or WalletTransactionType.Earn);
    }

    [Fact]
    public async Task RunAsync_RepeatedAndConcurrentRetriesCreateNoDuplicateFinancialEffect()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "retry", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));

        var runs = await Task.WhenAll(
            Phase7ExpiryEligibilityTests.RunAsync(factory.Services, now),
            Phase7ExpiryEligibilityTests.RunAsync(factory.Services, now));
        await Phase7ExpiryEligibilityTests.RunAsync(factory.Services, now);

        Assert.Equal(1, runs.Sum(item => item.ExpiredCount));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await context.WalletTransactions.CountAsync());
        Assert.Equal(2, await context.WalletLedgerEntries.CountAsync());
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == seed.WalletId);
        Assert.Equal((150m, 0m), (wallet.AvailableBalance, wallet.ReservedBalance));
    }

    [Fact]
    public async Task RunAsync_InsufficientReservedBalanceFailsWithoutChangingAnyState()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 25m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "inconsistent", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));

        var result = await Phase7ExpiryEligibilityTests.RunAsync(factory.Services, now);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(DeliveryJobRunStatus.Failed, result.Outcome);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal((DeliveryStatus.Active, ReservationStatus.Reserved), (delivery.Status, delivery.ReservationStatus));
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == seed.WalletId);
        Assert.Equal((100m, 25m), (wallet.AvailableBalance, wallet.ReservedBalance));
        Assert.Empty(await context.WalletTransactions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.WalletLedgerEntries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RunAsync_ForcedPersistenceFailureRollsBackDeliveryWalletTransactionAndLedger()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "rollback", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));

        using (var setupScope = factory.Services.CreateScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_Phase7_ForceLedgerFailure] ON [WalletLedgerEntries] INSTEAD OF INSERT AS THROW 51000, 'forced test failure', 1;");
        }

        var result = await Phase7ExpiryEligibilityTests.RunAsync(factory.Services, now);

        Assert.Equal(1, result.FailedCount);
        using var scope = factory.Services.CreateScope();
        var verification = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await verification.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Phase7_ForceLedgerFailure];");
        var delivery = await verification.DoctorAdDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal((DeliveryStatus.Active, ReservationStatus.Reserved), (delivery.Status, delivery.ReservationStatus));
        var wallet = await verification.Wallets.AsNoTracking().SingleAsync(item => item.Id == seed.WalletId);
        Assert.Equal((100m, 50m), (wallet.AvailableBalance, wallet.ReservedBalance));
        Assert.False(await verification.WalletTransactions.AnyAsync());
        Assert.False(await verification.WalletLedgerEntries.AnyAsync());
    }

    [Fact]
    public async Task RunAsync_FailedFirstCandidateDoesNotContaminateLaterSuccessfulCandidate()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 100m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "first-fails", new DateOnly(2026, 6, 30), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-2));
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "second-succeeds", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));
        var failedDeliveryId = seed.DeliveryIds["first-fails"];

        using (var setupScope = factory.Services.CreateScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [TR_Phase7_FailSelectedDeliveryLedger]
                ON [WalletLedgerEntries]
                AFTER INSERT
                AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE [MessageDeliveryId] LIKE 'delivery-first-fails-%')
                        THROW 51001, 'forced selected delivery failure', 1;
                END
                """);
        }

        var result = await Phase7ExpiryEligibilityTests.RunAsync(factory.Services, now);

        using var scope = factory.Services.CreateScope();
        var verification = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await verification.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Phase7_FailSelectedDeliveryLedger];");
        Assert.Equal((2, 1, 0, 1), (result.ExaminedCount, result.ExpiredCount, result.SkippedCount, result.FailedCount));
        var deliveries = await verification.DoctorAdDeliveries.AsNoTracking().ToDictionaryAsync(item => item.Id);
        Assert.Equal((DeliveryStatus.Active, ReservationStatus.Reserved), (deliveries[failedDeliveryId].Status, deliveries[failedDeliveryId].ReservationStatus));
        var successfulDeliveryId = seed.DeliveryIds["second-succeeds"];
        Assert.Equal((DeliveryStatus.Expired, ReservationStatus.Released), (deliveries[successfulDeliveryId].Status, deliveries[successfulDeliveryId].ReservationStatus));
        Assert.Single(await verification.WalletTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(2, await verification.WalletLedgerEntries.CountAsync());
        var wallet = await verification.Wallets.AsNoTracking().SingleAsync(item => item.Id == seed.WalletId);
        Assert.Equal((150m, 50m), (wallet.AvailableBalance, wallet.ReservedBalance));
    }
}
