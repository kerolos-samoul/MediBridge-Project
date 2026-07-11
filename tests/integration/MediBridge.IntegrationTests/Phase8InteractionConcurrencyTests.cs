using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionConcurrencyTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ConcurrentSameKeyAcceptRequests_ConvergeOnOneFinancialSettlement()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "race");

        var first = CreateDoctorClient(factory, fixture.DoctorUserId, "race-key-0001");
        var second = CreateDoctorClient(factory, fixture.DoctorUserId, "race-key-0001");
        try
        {
            var firstTask = PostAcceptAsync(first, fixture.DeliveryId);
            var secondTask = PostAcceptAsync(second, fixture.DeliveryId);
            var responses = await Task.WhenAll(firstTask, secondTask);

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            foreach (var response in responses)
            {
                response.Dispose();
            }

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
            Assert.Equal(DeliveryStatus.Accepted, delivery.Status);
            Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
            Assert.Equal(2, await db.WalletTransactions.AsNoTracking().CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
            Assert.Equal(2, await db.WalletLedgerEntries.AsNoTracking().CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
            Assert.Equal(1, await db.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentAcceptAndRejectRequests_ConvergeOnOneOutcomeAndOneFinancialSettlement()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "race-conflict");

        var acceptClient = CreateDoctorClient(factory, fixture.DoctorUserId, "race-accept-key-0001");
        var rejectClient = CreateDoctorClient(factory, fixture.DoctorUserId, "race-reject-key-0001");
        try
        {
            var acceptTask = PostInteractionAsync(acceptClient, fixture.DeliveryId, "Accept");
            var rejectTask = PostInteractionAsync(rejectClient, fixture.DeliveryId, "Reject");
            var responses = await Task.WhenAll(acceptTask, rejectTask);

            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            foreach (var response in responses)
            {
                response.Dispose();
            }

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
            Assert.True(delivery.Status is DeliveryStatus.Accepted or DeliveryStatus.Rejected);
            Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
            Assert.Equal(2, await db.WalletTransactions.AsNoTracking().CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
            Assert.Equal(2, await db.WalletLedgerEntries.AsNoTracking().CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
            Assert.Equal(1, await db.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
        }
        finally
        {
            acceptClient.Dispose();
            rejectClient.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentReadAndInteractRequests_BothCompleteWithoutDuplicateFinancialEffects()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "race-read");

        using var readClient = factory.CreateClient();
        readClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));
        using var interactClient = CreateDoctorClient(factory, fixture.DoctorUserId, "race-read-interact-key-0001");

        var readTask = readClient.PutAsync($"/api/doctor/messages/{fixture.DeliveryId}/read", null);
        var interactTask = PostAcceptAsync(interactClient, fixture.DeliveryId);
        using var readResponse = await readTask;
        using var interactResponse = await interactTask;

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, interactResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Accepted, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
        Assert.NotNull(delivery.ReadAtUtc);
        Assert.Equal(2, await db.WalletTransactions.AsNoTracking().CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
        Assert.Equal(2, await db.WalletLedgerEntries.AsNoTracking().CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
        Assert.Equal(1, await db.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
    }

    [Fact]
    public async Task SettledReplayWithMissingLedgerEvidence_ReturnsSafeAnomalyWithoutAdditionalMutation()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "partial-replay");
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "partial-replay-key-0001");
        using var first = await PostAcceptAsync(client, fixture.DeliveryId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var ledger = await db.WalletLedgerEntries.FirstAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId);
            db.WalletLedgerEntries.Remove(ledger);
            await db.SaveChangesAsync();
        }

        using var replay = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, replay.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(2, await verifyDb.WalletTransactions.AsNoTracking().CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
        Assert.Equal(1, await verifyDb.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
    }

    [Fact]
    public async Task FailureBeforeWalletMutation_LeavesDeliveryWalletTransactionsAndLedgerUnchanged()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "rollback-before-stage");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var delivery = await db.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
            delivery.PlatformFeeAmount = 12.34m;
            delivery.DoctorEarnings = 87.66m;
            await db.SaveChangesAsync();
        }

        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "rollback-before-stage-key-0001");
        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var dbVerify = verifyScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var deliveryVerify = await dbVerify.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Active, deliveryVerify.Status);
        Assert.Equal(ReservationStatus.Reserved, deliveryVerify.ReservationStatus);
        Assert.Equal(0, await dbVerify.WalletTransactions.AsNoTracking().CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
        Assert.Equal(0, await dbVerify.WalletLedgerEntries.AsNoTracking().CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
        Assert.Equal(0, await dbVerify.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
        var companyWallet = await dbVerify.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == fixture.CompanyId);
        var doctorWallet = await dbVerify.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Doctor && wallet.OwnerId == fixture.DoctorId);
        Assert.Equal(100m, companyWallet.ReservedBalance);
        Assert.Equal(5m, doctorWallet.AvailableBalance);
    }

    [Fact]
    public async Task FailureAfterStagedWalletChanges_RollsBackDeliveryWalletTransactionsAndLedger()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "rollback-after-stage");
        await SeedConflictingInteractionEvidenceAsync(factory.Services, fixture);
        using var client = CreateDoctorClient(factory, fixture.DoctorUserId, "rollback-after-stage-key-0001");

        using var response = await PostAcceptAsync(client, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.Equal(ReservationStatus.Reserved, delivery.ReservationStatus);
        Assert.Null(delivery.InteractedAtUtc);
        Assert.Equal(0, await db.WalletTransactions.AsNoTracking().CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
        Assert.Equal(0, await db.WalletLedgerEntries.AsNoTracking().CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
        Assert.Equal(1, await db.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
        var companyWallet = await db.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == fixture.CompanyId);
        var doctorWallet = await db.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Doctor && wallet.OwnerId == fixture.DoctorId);
        Assert.Equal(100m, companyWallet.ReservedBalance);
        Assert.Equal(5m, doctorWallet.AvailableBalance);
    }

    private static HttpClient CreateDoctorClient(Phase8ReadTrackingIntegrationTests.FixedClockFactory factory, string userId, string idempotencyKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", userId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        return client;
    }

    private static Task<HttpResponseMessage> PostAcceptAsync(HttpClient client, string deliveryId)
    {
        return PostInteractionAsync(client, deliveryId, "Accept");
    }

    private static Task<HttpResponseMessage> PostInteractionAsync(HttpClient client, string deliveryId, string outcome)
    {
        return client.PostAsync(
            $"/api/doctor/messages/{deliveryId}/interact",
            new StringContent($$"""{"Outcome":"{{outcome}}","Feedback":"Concurrent useful feedback."}""", Encoding.UTF8, "application/json"));
    }

    private static async Task SeedConflictingInteractionEvidenceAsync(
        IServiceProvider services,
        Phase8ReadTrackingIntegrationTests.Phase8Fixture fixture)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var companyWallet = await db.Wallets.SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == fixture.CompanyId);
        var doctorWallet = await db.Wallets.SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Doctor && wallet.OwnerId == fixture.DoctorId);
        var chargeId = $"conflict-charge-{Guid.NewGuid():N}";
        var earnId = $"conflict-earn-{Guid.NewGuid():N}";
        db.WalletTransactions.AddRange(
            new WalletTransaction
            {
                Id = chargeId,
                WalletId = companyWallet.Id,
                OperationType = WalletTransactionType.Charge,
                IdempotencyKey = $"conflict-charge-key-{Guid.NewGuid():N}",
                Amount = 1m,
                CreatedAtUtc = UtcNow
            },
            new WalletTransaction
            {
                Id = earnId,
                WalletId = doctorWallet.Id,
                OperationType = WalletTransactionType.Earn,
                IdempotencyKey = $"conflict-earn-key-{Guid.NewGuid():N}",
                Amount = 1m,
                CreatedAtUtc = UtcNow
            });
        db.DeliveryInteractions.Add(new DeliveryInteraction
        {
            Id = $"conflict-interaction-{Guid.NewGuid():N}",
            DeliveryId = fixture.DeliveryId,
            DoctorId = fixture.DoctorId,
            ActorUserId = fixture.DoctorUserId,
            Outcome = DeliveryInteractionOutcome.Accept,
            IdempotencyKeyHash = $"conflict-hash-{Guid.NewGuid():N}",
            RequestFingerprint = $"conflict-fingerprint-{Guid.NewGuid():N}",
            ChargeTransactionId = chargeId,
            EarnTransactionId = earnId,
            CreatedAtUtc = UtcNow
        });
        await db.SaveChangesAsync();
    }
}
