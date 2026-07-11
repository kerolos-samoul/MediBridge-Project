using System.Net;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionIdempotencyIntegrationTests
{
    [Fact]
    public async Task Interact_SameKeySamePayloadReplaysWithoutDuplicateSettlementOrAuditEffect()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "same-key-replay");

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var firstRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "same-key-0001",
            "Accept",
            " useful ");
        using var firstResponse = await client.SendAsync(firstRequest);
        var firstData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(firstResponse);
        var firstSnapshot = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        using var replayRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            " same-key-0001 ",
            " Accept ",
            "useful");
        using var replayResponse = await client.SendAsync(replayRequest);
        var replayData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(replayResponse);
        var replaySnapshot = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        Assert.Equal("Created", firstData.GetProperty("IdempotencyStatus").GetString());
        Assert.Equal("Replayed", replayData.GetProperty("IdempotencyStatus").GetString());
        Assert.Equal(firstData.GetProperty("InteractedAtUtc").GetDateTime(), replayData.GetProperty("InteractedAtUtc").GetDateTime());
        Assert.Equal(DeliveryStatus.Accepted, replaySnapshot.Status);
        Assert.Equal(firstSnapshot with { ReplayAuditCount = replaySnapshot.ReplayAuditCount }, replaySnapshot);
        Assert.Equal(2, replaySnapshot.WalletTransactionCount);
        Assert.Equal(2, replaySnapshot.WalletLedgerEntryCount);
        Assert.Equal(1, replaySnapshot.InteractionOperationCount);
        Assert.Equal(1, replaySnapshot.SettlementAuditCount);
        Assert.Equal(1, replaySnapshot.ReplayAuditCount);
    }

    [Fact]
    public async Task Interact_SameKeyDifferentPayloadConflicts_AndDifferentKeyClassifiesSettledDelivery()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "conflict");

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var firstRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "conflict-key-0001",
            "Reject",
            "first feedback");
        using var firstResponse = await client.SendAsync(firstRequest);
        await Phase8InteractionTestHelpers.ReadSuccessDataAsync(firstResponse);
        var settled = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        using var sameKeyDifferentDecision = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "conflict-key-0001",
            "Accept",
            "first feedback");
        using var sameKeyDifferentDecisionResponse = await client.SendAsync(sameKeyDifferentDecision);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(sameKeyDifferentDecisionResponse, HttpStatusCode.Conflict);

        using var sameKeyDifferentFeedback = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "conflict-key-0001",
            "Reject",
            "changed feedback");
        using var sameKeyDifferentFeedbackResponse = await client.SendAsync(sameKeyDifferentFeedback);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(sameKeyDifferentFeedbackResponse, HttpStatusCode.Conflict);

        using var differentKeySameDecision = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "different-key-0001",
            "Reject",
            null);
        using var differentKeySameDecisionResponse = await client.SendAsync(differentKeySameDecision);
        var replayData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(differentKeySameDecisionResponse);
        Assert.Equal("Replayed", replayData.GetProperty("IdempotencyStatus").GetString());
        Assert.Equal("Rejected", replayData.GetProperty("Status").GetString());

        using var differentKeyDifferentDecision = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "different-key-0002",
            "Accept",
            null);
        using var differentKeyDifferentDecisionResponse = await client.SendAsync(differentKeyDifferentDecision);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(differentKeyDifferentDecisionResponse, HttpStatusCode.Conflict);

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal(settled.Status, after.Status);
        Assert.Equal(settled.ReservationStatus, after.ReservationStatus);
        Assert.Equal(settled.InteractedAtUtc, after.InteractedAtUtc);
        Assert.Equal(settled.FeedbackText, after.FeedbackText);
        Assert.Equal(settled.CompanyReservedBalance, after.CompanyReservedBalance);
        Assert.Equal(settled.DoctorAvailableBalance, after.DoctorAvailableBalance);
        Assert.Equal(settled.WalletTransactionCount, after.WalletTransactionCount);
        Assert.Equal(settled.WalletLedgerEntryCount, after.WalletLedgerEntryCount);
        Assert.Equal(1, after.SettlementAuditCount);
        Assert.Equal(1, after.ReplayAuditCount);
        Assert.Equal(3, after.ConflictAuditCount);
    }
}
