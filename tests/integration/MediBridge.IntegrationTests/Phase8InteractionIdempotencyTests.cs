using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionIdempotencyTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SameKeySameContent_ReplaysWithoutDuplicateFinancialEffects()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "idem-replay");
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "idempotency-replay-key-0001");

        using var first = await PostInteractionAsync(client, fixture.DeliveryId, "Accept", "Helpful message for patients.");
        using var replay = await PostInteractionAsync(client, fixture.DeliveryId, "Accept", "Helpful message for patients.");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.False(firstDocument.RootElement.GetProperty("Data").GetProperty("Replayed").GetBoolean());
        Assert.True(replayDocument.RootElement.GetProperty("Data").GetProperty("Replayed").GetBoolean());
        await AssertSingleFinancialSettlementAsync(factory.Services, fixture.DeliveryId);
    }

    [Fact]
    public async Task SameKeyDifferentContent_ConflictsWithoutDuplicateFinancialEffects_AndRedactsRawKey()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "idem");
        using var client = factory.CreateClient();
        const string rawKey = "idempotency-conflict-key-0001";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", rawKey);

        using var accepted = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Accept","Feedback":"Helpful message for patients."}""", Encoding.UTF8, "application/json"));
        using var conflicting = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Reject","Feedback":"Helpful message for patients."}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        var conflictBody = await conflicting.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawKey, conflictBody, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(2, await db.WalletTransactions.CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
        Assert.Equal(2, await db.WalletLedgerEntries.CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
        var interaction = await db.DeliveryInteractions.SingleAsync(interaction => interaction.DeliveryId == fixture.DeliveryId);
        Assert.DoesNotContain(rawKey, interaction.IdempotencyKeyHash, StringComparison.Ordinal);
        Assert.DoesNotContain(rawKey, interaction.RequestFingerprint, StringComparison.Ordinal);
        var auditText = string.Join('\n', await db.AuditEvents
            .Where(audit => audit.TargetId == fixture.DeliveryId)
            .Select(audit => (audit.Metadata ?? string.Empty) + " " + (audit.Reason ?? string.Empty))
            .ToListAsync());
        Assert.DoesNotContain(rawKey, auditText, StringComparison.Ordinal);
        Assert.Contains("idempotency-fingerprint-conflict", auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameKeyDifferentFeedback_ConflictsWithoutDuplicateFinancialEffects()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "idem-feedback");
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "idempotency-feedback-key-0001");

        using var accepted = await PostInteractionAsync(client, fixture.DeliveryId, "Accept", "Helpful message for patients.");
        using var conflicting = await PostInteractionAsync(client, fixture.DeliveryId, "Accept", "Different useful feedback.");

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        await AssertSingleFinancialSettlementAsync(factory.Services, fixture.DeliveryId);
    }

    [Fact]
    public async Task DifferentKeyMatchingSettledRequest_ReplaysCompletedFinancialSettlement()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "idem-different-key-replay");
        using var firstClient = CreateDoctorClient(factory, fixture.DoctorUserId, "idempotency-first-key-0001");
        using var replayClient = CreateDoctorClient(factory, fixture.DoctorUserId, "idempotency-second-key-0001");

        using var first = await PostInteractionAsync(firstClient, fixture.DeliveryId, "Reject", "Helpful message for patients.");
        using var replay = await PostInteractionAsync(replayClient, fixture.DeliveryId, "Reject", "Helpful message for patients.");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayDocument.RootElement.GetProperty("Data").GetProperty("Replayed").GetBoolean());
        await AssertSingleFinancialSettlementAsync(factory.Services, fixture.DeliveryId);
    }

    [Fact]
    public async Task DifferentKeyConflictingSettledRequest_ConflictsWithoutDuplicateFinancialEffects()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "idem-different-key-conflict");
        using var firstClient = CreateDoctorClient(factory, fixture.DoctorUserId, "idempotency-first-conflict-key-0001");
        using var conflictClient = CreateDoctorClient(factory, fixture.DoctorUserId, "idempotency-second-conflict-key-0001");

        using var first = await PostInteractionAsync(firstClient, fixture.DeliveryId, "Accept", "Helpful message for patients.");
        using var conflict = await PostInteractionAsync(conflictClient, fixture.DeliveryId, "Reject", "Helpful message for patients.");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertSingleFinancialSettlementAsync(factory.Services, fixture.DeliveryId);
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

    private static Task<HttpResponseMessage> PostInteractionAsync(HttpClient client, string deliveryId, string outcome, string feedback)
    {
        return client.PostAsync(
            $"/api/doctor/messages/{deliveryId}/interact",
            new StringContent($$"""{"Outcome":"{{outcome}}","Feedback":"{{feedback}}"}""", Encoding.UTF8, "application/json"));
    }

    private static async Task AssertSingleFinancialSettlementAsync(IServiceProvider services, string deliveryId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(2, await db.WalletTransactions.CountAsync(transaction => transaction.RelatedDeliveryId == deliveryId));
        Assert.Equal(2, await db.WalletLedgerEntries.CountAsync(entry => entry.MessageDeliveryId == deliveryId));
        Assert.Equal(1, await db.DeliveryInteractions.CountAsync(interaction => interaction.DeliveryId == deliveryId));
    }
}
