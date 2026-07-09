using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7ExpiryEligibilityTests
{
    [Fact]
    public async Task RunAsync_CatchesUpEveryOverdueEligibleDeliveryAndLeavesIneligibleRowsUnchanged()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedOwnerGraphAsync(factory.Services, reservedBalance: 100m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);

        await SeedDeliveryAsync(factory.Services, seed, "oldest", new DateOnly(2026, 6, 20), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-12));
        await SeedDeliveryAsync(factory.Services, seed, "yesterday", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));
        await SeedDeliveryAsync(factory.Services, seed, "today", new DateOnly(2026, 7, 2), DeliveryStatus.Active, ReservationStatus.Reserved, now);
        await SeedDeliveryAsync(factory.Services, seed, "accepted", new DateOnly(2026, 7, 1), DeliveryStatus.Accepted, ReservationStatus.Charged, now.AddDays(-1));
        await SeedDeliveryAsync(factory.Services, seed, "rejected", new DateOnly(2026, 7, 1), DeliveryStatus.Rejected, ReservationStatus.Released, now.AddDays(-1));
        await SeedDeliveryAsync(factory.Services, seed, "expired", new DateOnly(2026, 7, 1), DeliveryStatus.Expired, ReservationStatus.Released, now.AddDays(-1));

        using (var orderingScope = factory.Services.CreateScope())
        {
            var repository = orderingScope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
            var first = Assert.Single(await repository.ListOverduePageAsync(new DateOnly(2026, 7, 2), null, 1));
            var second = Assert.Single(await repository.ListOverduePageAsync(
                new DateOnly(2026, 7, 2),
                new OverdueDeliveryCursor(first.DeliveryDateEgypt, first.CreatedAtUtc, first.Id),
                1));
            Assert.Equal(seed.DeliveryIds["oldest"], first.Id);
            Assert.Equal(seed.DeliveryIds["yesterday"], second.Id);
        }

        var result = await RunAsync(factory.Services, now);

        Assert.Equal(new DateOnly(2026, 7, 2), result.BusinessDateEgypt);
        Assert.Equal(2, result.ExaminedCount);
        Assert.Equal(2, result.ExpiredCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(DeliveryJobRunStatus.Succeeded, result.Outcome);

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var deliveries = await context.DoctorAdDeliveries.AsNoTracking().ToDictionaryAsync(item => item.Id);
        foreach (var name in new[] { "oldest", "yesterday" })
        {
            var delivery = deliveries[seed.DeliveryIds[name]];
            Assert.Equal(DeliveryStatus.Expired, delivery.Status);
            Assert.Equal(ReservationStatus.Released, delivery.ReservationStatus);
            Assert.Equal(now, delivery.ExpiredAtUtc);
            Assert.Equal(now, delivery.UpdatedAtUtc);
        }

        Assert.Equal(DeliveryStatus.Active, deliveries[seed.DeliveryIds["today"]].Status);
        Assert.Equal(DeliveryStatus.Accepted, deliveries[seed.DeliveryIds["accepted"]].Status);
        Assert.Equal(DeliveryStatus.Rejected, deliveries[seed.DeliveryIds["rejected"]].Status);
        Assert.Equal(DeliveryStatus.Expired, deliveries[seed.DeliveryIds["expired"]].Status);
    }

    [Fact]
    public async Task RunAsync_UsesTheCapturedCairoDateAtMidnightBoundary()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);
        var beforeMidnightUtc = new DateTime(2026, 7, 2, 20, 59, 0, DateTimeKind.Utc);
        await SeedDeliveryAsync(factory.Services, seed, "boundary", new DateOnly(2026, 7, 2), DeliveryStatus.Active, ReservationStatus.Reserved, beforeMidnightUtc);

        var before = await RunAsync(factory.Services, beforeMidnightUtc);
        var after = await RunAsync(factory.Services, beforeMidnightUtc.AddMinutes(1));

        Assert.Equal(new DateOnly(2026, 7, 2), before.BusinessDateEgypt);
        Assert.Equal(0, before.ExaminedCount);
        Assert.Equal(new DateOnly(2026, 7, 3), after.BusinessDateEgypt);
        Assert.Equal(1, after.ExpiredCount);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync(item => item.Id == seed.DeliveryIds["boundary"]);
        Assert.Equal(beforeMidnightUtc.AddMinutes(1), delivery.ExpiredAtUtc);
    }

    [Fact]
    public async Task OverdueCursor_ContinuesByIdWhenDateAndCreationInstantAreEqual()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedOwnerGraphAsync(factory.Services, reservedBalance: 100m);
        var createdAtUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        await SeedDeliveryAsync(factory.Services, seed, "alpha", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, createdAtUtc);
        await SeedDeliveryAsync(factory.Services, seed, "beta", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, createdAtUtc);
        var expected = seed.DeliveryIds.Values.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<MediBridge.Core.Interfaces.Messaging.IDeliveryRepository>();
        var first = Assert.Single(await repository.ListOverduePageAsync(new DateOnly(2026, 7, 2), null, 1));
        var second = Assert.Single(await repository.ListOverduePageAsync(
            new DateOnly(2026, 7, 2),
            new MediBridge.Core.Interfaces.Messaging.OverdueDeliveryCursor(first.DeliveryDateEgypt, first.CreatedAtUtc, first.Id),
            1));

        Assert.Equal(expected[0], first.Id);
        Assert.Equal(expected[1], second.Id);
    }

    internal static async Task<ExpirySeed> SeedOwnerGraphAsync(IServiceProvider services, decimal reservedBalance)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var now = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var doctorUserId = $"doctor-user-{suffix}";
        var doctorId = $"doctor-{suffix}";
        var companyUserId = $"company-user-{suffix}";
        var companyId = $"company-{suffix}";
        await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(services, doctorUserId, doctorId, $"{doctorId}@example.com", 50m, 10, now);
        await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(services, companyUserId, companyId, $"{companyId}@example.com", now);
        await Phase7DeliveryTestHelpers.SeedCompanyWalletAsync(services, $"wallet-{suffix}", companyId, companyUserId, 100m, reservedBalance, now);
        return new ExpirySeed(doctorId, companyId, $"wallet-{suffix}", new Dictionary<string, string>());
    }

    internal static async Task SeedDeliveryAsync(
        IServiceProvider services,
        ExpirySeed seed,
        string name,
        DateOnly deliveryDate,
        DeliveryStatus status,
        ReservationStatus reservationStatus,
        DateTime createdAtUtc)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var campaignId = $"campaign-{name}-{suffix}";
        var deliveryId = $"delivery-{name}-{suffix}";
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(services, campaignId, seed.CompanyId, CampaignStatus.Approved, createdAtUtc.AddHours(-1), createdAtUtc.AddHours(-2), false, null);
        await Phase7DeliveryTestHelpers.SeedDeliveryAsync(
            services,
            deliveryId,
            seed.DoctorId,
            campaignId,
            seed.CompanyId,
            deliveryDate,
            createdAtUtc,
            status,
            reservationStatus,
            50m,
            20m,
            10m,
            40m,
            50m,
            createdAtUtc);
        seed.DeliveryIds.Add(name, deliveryId);
    }

    internal static async Task<MediBridge.Services.DTOs.Messaging.DeliveryJobResultDto> RunAsync(IServiceProvider services, DateTime utcNow)
    {
        using var scope = services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var clock = new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(utcNow)));
        IDeliveryExpiryService service = new DeliveryExpiryService(unitOfWork, clock);
        return await service.RunAsync(CancellationToken.None);
    }

    internal sealed record ExpirySeed(string DoctorId, string CompanyId, string WalletId, Dictionary<string, string> DeliveryIds);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => now;
    }
}
