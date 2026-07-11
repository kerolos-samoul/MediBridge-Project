using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionAuditSafetyTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SuccessfulSettlement_AuditsSafeCategoryWithoutRawKeyOrWalletInternals()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "audit-success");
        const string rawKey = "audit-success-key-0001";
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, rawKey);

        using var response = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Accept","Feedback":"Useful audit feedback."}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawKey, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("AvailableBalance", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservedBalance", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("WalletId", responseBody, StringComparison.Ordinal);

        var auditText = await ReadAuditTextAsync(factory.Services, fixture.DeliveryId);
        Assert.Contains("settlement-succeeded", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(rawKey, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("AvailableBalance", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservedBalance", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("WalletId", auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdempotencyConflict_AuditsSafeConflictCategoryAndRedactsRawKey()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "audit-conflict");
        const string rawKey = "audit-conflict-key-0001";
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, rawKey);

        using var accepted = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Accept","Feedback":"Useful audit feedback."}""", Encoding.UTF8, "application/json"));
        using var conflicting = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Reject","Feedback":"Useful audit feedback."}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        var responseBody = await conflicting.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawKey, responseBody, StringComparison.Ordinal);
        var auditText = await ReadAuditTextAsync(factory.Services, fixture.DeliveryId);
        Assert.Contains("idempotency-fingerprint-conflict", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(rawKey, auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReservationSnapshotAnomaly_AuditsSafeCategoryWithoutFinancialMutation()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "audit-anomaly");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
            delivery.PlatformFeeAmount = 12.34m;
            delivery.DoctorEarnings = 87.66m;
            await db.SaveChangesAsync();
        }

        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "audit-anomaly-key-0001");
        using var response = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var dbVerify = verifyScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Empty(await dbVerify.WalletTransactions.Where(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId).ToListAsync());
        Assert.Empty(await dbVerify.DeliveryInteractions.Where(interaction => interaction.DeliveryId == fixture.DeliveryId).ToListAsync());
        var auditText = await ReadAuditTextAsync(factory.Services, fixture.DeliveryId);
        Assert.Contains("snapshot-formula-invalid", auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrdinaryRead_DoesNotCreateFinancialAudit()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "audit-read");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));

        using var response = await client.PutAsync($"/api/doctor/messages/{fixture.DeliveryId}/read", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Empty(await db.AuditEvents.Where(audit => audit.TargetId == fixture.DeliveryId).ToListAsync());
        Assert.Empty(await db.WalletTransactions.Where(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId).ToListAsync());
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

    private static async Task<string> ReadAuditTextAsync(IServiceProvider services, string deliveryId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var auditRows = await db.AuditEvents
            .Where(audit => audit.TargetId == deliveryId)
            .Select(audit => string.Join(' ', audit.EventType, audit.Outcome, audit.Reason, audit.Metadata))
            .ToListAsync();
        return string.Join('\n', auditRows);
    }
}
