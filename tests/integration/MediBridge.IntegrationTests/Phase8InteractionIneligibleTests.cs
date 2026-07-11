using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediBridge.Core.Enums;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionIneligibleTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UnsafeFeedback_IsRejectedBeforeAnyDomainOrFinancialMutation()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "unsafe");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "unsafe-feedback-key-0001");

        using var response = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Accept","Feedback":"[label](https://example.test)"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.Equal(ReservationStatus.Reserved, delivery.ReservationStatus);
        Assert.Null(delivery.FeedbackText);
        Assert.Empty(await db.WalletTransactions.Where(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId).ToListAsync());
        Assert.Empty(await db.WalletLedgerEntries.Where(entry => entry.MessageDeliveryId == fixture.DeliveryId).ToListAsync());
        Assert.Empty(await db.DeliveryInteractions.Where(interaction => interaction.DeliveryId == fixture.DeliveryId).ToListAsync());
    }

    [Fact]
    public async Task PriorDayActiveDelivery_IsNotInteractable_AndDoesNotMutateFinancialState()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "stale");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
            delivery.DeliveryDateEgypt = new DateOnly(2026, 7, 10);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "stale-delivery-key-0001");
        using var response = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Empty(await verifyDb.WalletTransactions.Where(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId).ToListAsync());
        Assert.Empty(await verifyDb.DeliveryInteractions.Where(interaction => interaction.DeliveryId == fixture.DeliveryId).ToListAsync());
    }

    [Theory]
    [InlineData(DeliveryStatus.Expired, ReservationStatus.Released)]
    [InlineData(DeliveryStatus.Active, ReservationStatus.Released)]
    [InlineData(DeliveryStatus.Accepted, ReservationStatus.Charged)]
    public async Task IneligibleDeliveryStates_ReturnSafeNoMutationResponse(DeliveryStatus status, ReservationStatus reservationStatus)
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, $"ineligible-{status}-{reservationStatus}");
        await MutateDeliveryAsync(factory.Services, fixture.DeliveryId, delivery =>
        {
            delivery.Status = status;
            delivery.ReservationStatus = reservationStatus;
            delivery.ExpiredAtUtc = status == DeliveryStatus.Expired ? UtcNow.AddMinutes(-1) : null;
            delivery.InteractedAtUtc = status is DeliveryStatus.Accepted or DeliveryStatus.Rejected ? UtcNow.AddMinutes(-1) : null;
        });
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, $"ineligible-state-key-{Guid.NewGuid():N}");

        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoNewFinancialMutationAsync(factory.Services, fixture.DeliveryId);
    }

    [Fact]
    public async Task DeletedDelivery_IsNotInteractable_AndDoesNotMutateFinancialState()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "deleted-delivery");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
            db.DoctorAdDeliveries.Remove(delivery);
            await db.SaveChangesAsync();
        }

        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "deleted-delivery-key-0001");
        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("insufficient-company-reserved")]
    [InlineData("missing-company-wallet")]
    [InlineData("deleted-company-wallet")]
    [InlineData("non-egp-company-wallet")]
    [InlineData("non-egp-doctor-wallet")]
    [InlineData("invalid-snapshot-formula")]
    public async Task ReservationSnapshotAndWalletAnomalies_ReturnSafe503AndNoSettlement(string scenario)
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, scenario);
        await ApplyAnomalyScenarioAsync(factory.Services, fixture, scenario);
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, $"anomaly-key-{Guid.NewGuid():N}");

        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("AvailableBalance", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservedBalance", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionString", body, StringComparison.OrdinalIgnoreCase);
        await AssertNoNewFinancialMutationAsync(factory.Services, fixture.DeliveryId);
    }

    private static HttpClient CreateDoctorClient(
        Phase8ReadTrackingIntegrationTests.FixedClockFactory factory,
        string userId,
        string idempotencyKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", userId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        return client;
    }

    private static Task<HttpResponseMessage> PostAcceptAsync(HttpClient client, string deliveryId)
    {
        return client.PostAsync(
            $"/api/doctor/messages/{deliveryId}/interact",
            new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json"));
    }

    private static async Task MutateDeliveryAsync(
        IServiceProvider services,
        string deliveryId,
        Action<MediBridge.Core.Entities.Messaging.DoctorAdDelivery> mutate)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == deliveryId);
        mutate(delivery);
        await db.SaveChangesAsync();
    }

    private static async Task ApplyAnomalyScenarioAsync(
        IServiceProvider services,
        Phase8ReadTrackingIntegrationTests.Phase8Fixture fixture,
        string scenario)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        switch (scenario)
        {
            case "insufficient-company-reserved":
            {
                var wallet = await FindWalletAsync(db, WalletOwnerType.Company, fixture.CompanyId);
                wallet.ReservedBalance = 99.99m;
                break;
            }
            case "missing-company-wallet":
            {
                var wallet = await FindWalletAsync(db, WalletOwnerType.Company, fixture.CompanyId);
                db.Wallets.Remove(wallet);
                break;
            }
            case "deleted-company-wallet":
            {
                var wallet = await FindWalletAsync(db, WalletOwnerType.Company, fixture.CompanyId);
                wallet.IsDeleted = true;
                wallet.DeletedAtUtc = UtcNow;
                break;
            }
            case "non-egp-company-wallet":
            {
                var wallet = await FindWalletAsync(db, WalletOwnerType.Company, fixture.CompanyId);
                wallet.Currency = "USD";
                break;
            }
            case "non-egp-doctor-wallet":
            {
                var wallet = await FindWalletAsync(db, WalletOwnerType.Doctor, fixture.DoctorId);
                wallet.Currency = "USD";
                break;
            }
            case "invalid-snapshot-formula":
            {
                var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
                delivery.PlatformFeeAmount = 12.34m;
                delivery.DoctorEarnings = 87.66m;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown anomaly scenario.");
        }

        await db.SaveChangesAsync();
    }

    private static Task<Wallet> FindWalletAsync(MediBridgeDbContext db, WalletOwnerType ownerType, string ownerId)
    {
        return db.Wallets.SingleAsync(wallet => wallet.OwnerType == ownerType && wallet.OwnerId == ownerId);
    }

    private static async Task AssertNoNewFinancialMutationAsync(IServiceProvider services, string deliveryId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, await db.WalletTransactions.CountAsync(transaction => transaction.RelatedDeliveryId == deliveryId));
        Assert.Equal(0, await db.WalletLedgerEntries.CountAsync(entry => entry.MessageDeliveryId == deliveryId));
        Assert.Equal(0, await db.DeliveryInteractions.CountAsync(interaction => interaction.DeliveryId == deliveryId));
    }
}
