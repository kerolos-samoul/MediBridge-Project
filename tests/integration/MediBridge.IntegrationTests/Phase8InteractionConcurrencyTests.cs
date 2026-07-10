using System.Net;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionConcurrencyTests
{
    [Fact]
    public async Task Interact_ConcurrentSameKeySamePayloadProducesOneCreatedAndReplayedRemainder()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "concurrent-same");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
            using var request = Phase8InteractionTestHelpers.CreateInteractRequest(
                seed.DeliveryId,
                "concurrent-same-0001",
                "Accept");
            var response = await client.SendAsync(request);
            var data = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(response);
            return data.GetProperty("IdempotencyStatus").GetString();
        }));

        Assert.Equal(1, responses.Count(status => status == "Created"));
        Assert.Equal(4, responses.Count(status => status == "Replayed"));

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal(2, after.WalletTransactionCount);
        Assert.Equal(2, after.WalletLedgerEntryCount);
        Assert.Equal(1, after.InteractionOperationCount);
        Assert.Equal(1, after.SettlementAuditCount);
    }

    [Fact]
    public async Task Interact_ConcurrentAcceptAndRejectProducesOneWinnerAndNoDuplicateFinancialEffects()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "concurrent-conflict");

        var statuses = await Task.WhenAll(
            SendAsync(factory, seed.DoctorUserId, seed.DeliveryId, "concurrent-a-0001", "Accept"),
            SendAsync(factory, seed.DoctorUserId, seed.DeliveryId, "concurrent-r-0001", "Reject"));

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.True(after.Status is MediBridge.Core.Enums.DeliveryStatus.Accepted or MediBridge.Core.Enums.DeliveryStatus.Rejected);
        Assert.Equal(MediBridge.Core.Enums.ReservationStatus.Charged, after.ReservationStatus);
        Assert.Equal(2, after.WalletTransactionCount);
        Assert.Equal(2, after.WalletLedgerEntryCount);
        Assert.Equal(1, after.SettlementAuditCount);
        Assert.Equal(0m, after.CompanyReservedBalance);
        Assert.Equal(65m, after.DoctorAvailableBalance);
    }

    private static async Task<HttpStatusCode> SendAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string doctorUserId,
        string deliveryId,
        string key,
        string decision)
    {
        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, doctorUserId);
        using var request = Phase8InteractionTestHelpers.CreateInteractRequest(deliveryId, key, decision);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            await Phase8InteractionTestHelpers.ReadSuccessDataAsync(response);
        }
        else
        {
            await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(response, HttpStatusCode.Conflict);
        }

        return response.StatusCode;
    }
}
