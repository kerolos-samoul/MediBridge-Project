using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8ReadTrackingIntegrationTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadTracking_RecordsFirstReadOnce_AndCreatesNoFinancialMutation()
    {
        await using var factory = new FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await SeedPhase8FixtureAsync(factory.Services, "read");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));

        var before = await SnapshotFinancialCountsAsync(factory.Services, fixture.DeliveryId);
        using var firstResponse = await client.PutAsync($"/api/doctor/messages/{fixture.DeliveryId}/read", null);
        using var secondResponse = await client.PutAsync($"/api/doctor/messages/{fixture.DeliveryId}/read", null);
        var after = await SnapshotFinancialCountsAsync(factory.Services, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var firstDocument = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondDocument = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var firstData = firstDocument.RootElement.GetProperty("Data");
        var secondData = secondDocument.RootElement.GetProperty("Data");
        Assert.False(firstData.GetProperty("AlreadyRead").GetBoolean());
        Assert.True(secondData.GetProperty("AlreadyRead").GetBoolean());
        Assert.Equal(firstData.GetProperty("ReadAtUtc").GetString(), secondData.GetProperty("ReadAtUtc").GetString());
        Assert.Equal(before, after);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.Equal(ReservationStatus.Reserved, delivery.ReservationStatus);
        Assert.NotNull(delivery.ReadAtUtc);
    }

    [Theory]
    [InlineData(DeliveryStatus.Accepted)]
    [InlineData(DeliveryStatus.Rejected)]
    public async Task ReadTracking_AllowsAlreadySettledCurrentDayDelivery_WithoutChangingOutcomeOrFinancialState(DeliveryStatus settledStatus)
    {
        await using var factory = new FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await SeedPhase8FixtureAsync(factory.Services, $"read-settled-{settledStatus}");
        await SetDeliveryStatusAsync(factory.Services, fixture.DeliveryId, settledStatus, ReservationStatus.Charged);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));

        var before = await SnapshotFinancialCountsAsync(factory.Services, fixture.DeliveryId);
        using var response = await client.PutAsync($"/api/doctor/messages/{fixture.DeliveryId}/read", null);
        var after = await SnapshotFinancialCountsAsync(factory.Services, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, after);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(settledStatus.ToString(), data.GetProperty("Status").GetString());
        Assert.False(data.GetProperty("AlreadyRead").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(settledStatus, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
        Assert.NotNull(delivery.ReadAtUtc);
    }

    [Fact]
    public async Task ReadTracking_DeniesPriorDayAndCrossDoctorDeliveries_WithoutMutationOrDisclosure()
    {
        await using var factory = new FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await SeedPhase8FixtureAsync(factory.Services, "read-deny");
        var otherDoctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            factory.Services,
            $"other-doctor-user-{Guid.NewGuid():N}",
            $"other-doctor-{Guid.NewGuid():N}",
            $"other-doctor-{Guid.NewGuid():N}@example.test",
            100m,
            10,
            UtcNow.AddDays(-5));
        var priorDayDeliveryId = $"prior-day-{Guid.NewGuid():N}";
        var crossDoctorDeliveryId = $"cross-doctor-{Guid.NewGuid():N}";
        await Phase7DeliveryTestHelpers.SeedPhase8ActiveReservedDeliveryAsync(
            factory.Services,
            priorDayDeliveryId,
            fixture.DoctorId,
            fixture.CampaignId,
            fixture.CompanyId,
            new DateOnly(2026, 7, 10),
            UtcNow.AddDays(-1),
            100m,
            12.345m,
            12.35m,
            87.65m);
        await Phase7DeliveryTestHelpers.SeedPhase8ActiveReservedDeliveryAsync(
            factory.Services,
            crossDoctorDeliveryId,
            otherDoctor.DoctorId,
            fixture.CampaignId,
            fixture.CompanyId,
            new DateOnly(2026, 7, 11),
            UtcNow.AddMinutes(-20),
            100m,
            12.345m,
            12.35m,
            87.65m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));

        var priorBefore = await SnapshotFinancialCountsAsync(factory.Services, priorDayDeliveryId);
        var crossBefore = await SnapshotFinancialCountsAsync(factory.Services, crossDoctorDeliveryId);
        using var priorResponse = await client.PutAsync($"/api/doctor/messages/{priorDayDeliveryId}/read", null);
        using var crossResponse = await client.PutAsync($"/api/doctor/messages/{crossDoctorDeliveryId}/read", null);
        var priorAfter = await SnapshotFinancialCountsAsync(factory.Services, priorDayDeliveryId);
        var crossAfter = await SnapshotFinancialCountsAsync(factory.Services, crossDoctorDeliveryId);

        Assert.Equal(HttpStatusCode.NotFound, priorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossResponse.StatusCode);
        Assert.Equal(priorBefore, priorAfter);
        Assert.Equal(crossBefore, crossAfter);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var deliveries = await db.DoctorAdDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.Id == priorDayDeliveryId || delivery.Id == crossDoctorDeliveryId)
            .ToDictionaryAsync(delivery => delivery.Id);
        Assert.Null(deliveries[priorDayDeliveryId].ReadAtUtc);
        Assert.Null(deliveries[crossDoctorDeliveryId].ReadAtUtc);
    }

    private static async Task<FinancialCounts> SnapshotFinancialCountsAsync(IServiceProvider services, string deliveryId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return new FinancialCounts(
            await db.WalletTransactions.CountAsync(transaction => transaction.RelatedDeliveryId == deliveryId),
            await db.WalletLedgerEntries.CountAsync(entry => entry.MessageDeliveryId == deliveryId),
            await db.DeliveryInteractions.CountAsync(interaction => interaction.DeliveryId == deliveryId),
            await db.AuditEvents.CountAsync(audit => audit.TargetId == deliveryId));
    }

    private static async Task SetDeliveryStatusAsync(
        IServiceProvider services,
        string deliveryId,
        DeliveryStatus status,
        ReservationStatus reservationStatus)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == deliveryId);
        delivery.Status = status;
        delivery.ReservationStatus = reservationStatus;
        delivery.InteractedAtUtc = UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
    }

    internal static async Task<Phase8Fixture> SeedPhase8FixtureAsync(IServiceProvider services, string suffixPrefix)
    {
        var suffix = $"{suffixPrefix}-{Guid.NewGuid():N}";
        var doctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            services, $"doctor-user-{suffix}", $"doctor-{suffix}", $"doctor-{suffix}@example.test", 100m, 10, UtcNow.AddDays(-5));
        var company = await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            services, $"company-user-{suffix}", $"company-{suffix}", $"company-{suffix}@example.test", UtcNow.AddDays(-5));
        var campaignId = $"campaign-{suffix}";
        var deliveryId = $"delivery-{suffix}";
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            services, campaignId, company.CompanyId, CampaignStatus.Approved, UtcNow.AddDays(-2), UtcNow.AddDays(-3), false, null);
        await Phase7DeliveryTestHelpers.SeedCompanyWalletAsync(
            services, $"company-wallet-{suffix}", company.CompanyId, company.UserId, availableBalance: 0m, reservedBalance: 100m, UtcNow.AddDays(-1));
        await Phase7DeliveryTestHelpers.SeedDoctorWalletAsync(
            services, $"doctor-wallet-{suffix}", doctor.DoctorId, doctor.UserId, availableBalance: 5m, reservedBalance: 0m, UtcNow.AddDays(-1));
        await Phase7DeliveryTestHelpers.SeedPhase8ActiveReservedDeliveryAsync(
            services, deliveryId, doctor.DoctorId, campaignId, company.CompanyId, new DateOnly(2026, 7, 11), UtcNow.AddMinutes(-30),
            price: 100m, platformFeePercent: 12.345m, platformFee: 12.35m, doctorEarnings: 87.65m);
        return new Phase8Fixture(doctor.UserId, doctor.DoctorId, company.CompanyId, campaignId, deliveryId);
    }

    private sealed record FinancialCounts(int Transactions, int LedgerEntries, int Interactions, int Audits);

    internal sealed record Phase8Fixture(string DoctorUserId, string DoctorId, string CompanyId, string CampaignId, string DeliveryId);

    internal sealed class FixedClockFactory(DateTime utcNow) : ConfiguredWebAppFactory
    {
        public AdjustableTimeProvider Clock { get; } = new(utcNow);

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
    }

    internal sealed class AdjustableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTime currentUtc = utcNow;
        public override DateTimeOffset GetUtcNow() => new(currentUtc);
        public void SetUtcNow(DateTime value) => currentUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
