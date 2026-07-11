using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionBusinessDateTests
{
    [Theory]
    [InlineData(2026, 7, 10)]
    [InlineData(2026, 7, 12)]
    public async Task NonCurrentBusinessDateDeliveries_AreNotInteractable(int year, int month, int day)
    {
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(utcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, $"business-date-{year}-{month}-{day}");
        await SetDeliveryDateAsync(factory.Services, fixture.DeliveryId, new DateOnly(year, month, day));
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, $"business-date-key-{Guid.NewGuid():N}");

        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoMutationAsync(factory.Services, fixture.DeliveryId);
    }

    [Fact]
    public async Task CairoDstBoundary_UsesEgyptLocalBusinessDate()
    {
        var utcNow = new DateTime(2026, 4, 24, 22, 30, 0, DateTimeKind.Utc);
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(utcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await SeedFixtureForDateAsync(factory.Services, "dst-boundary", new DateOnly(2026, 4, 25), utcNow);
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "dst-boundary-key-0001");

        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DelayedExpiryPriorDayActiveDelivery_IsNotInteractableUntilPhase7ExpiryJobHandlesIt()
    {
        var utcNow = new DateTime(2026, 7, 11, 1, 0, 0, DateTimeKind.Utc);
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(utcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "delayed-expiry");
        await SetDeliveryDateAsync(factory.Services, fixture.DeliveryId, new DateOnly(2026, 7, 10));
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "delayed-expiry-key-0001");

        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.Equal(ReservationStatus.Reserved, delivery.ReservationStatus);
        Assert.Null(delivery.InteractedAtUtc);
    }

    private static async Task<Phase8ReadTrackingIntegrationTests.Phase8Fixture> SeedFixtureForDateAsync(
        IServiceProvider services,
        string suffixPrefix,
        DateOnly deliveryDateEgypt,
        DateTime utcNow)
    {
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(services, suffixPrefix);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        delivery.DeliveryDateEgypt = deliveryDateEgypt;
        delivery.DeliveredAtUtc = utcNow.AddMinutes(-30);
        await db.SaveChangesAsync();
        return fixture;
    }

    private static async Task SetDeliveryDateAsync(IServiceProvider services, string deliveryId, DateOnly deliveryDateEgypt)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == deliveryId);
        delivery.DeliveryDateEgypt = deliveryDateEgypt;
        await db.SaveChangesAsync();
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

    private static async Task AssertNoMutationAsync(IServiceProvider services, string deliveryId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, await db.WalletTransactions.CountAsync(transaction => transaction.RelatedDeliveryId == deliveryId));
        Assert.Equal(0, await db.WalletLedgerEntries.CountAsync(entry => entry.MessageDeliveryId == deliveryId));
        Assert.Equal(0, await db.DeliveryInteractions.CountAsync(interaction => interaction.DeliveryId == deliveryId));
    }
}
