using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionFeedbackIntegrationTests
{
    [Fact]
    public async Task Interact_OmittedAndWhitespaceFeedback_SettleWithNoStoredFeedback()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var omitted = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "feedback-omitted");
        var whitespace = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "feedback-whitespace");

        using var omittedClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, omitted.DoctorUserId);
        using var whitespaceClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, whitespace.DoctorUserId);
        using var omittedRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/doctor/messages/{omitted.DeliveryId}/interact")
        {
            Content = JsonContent.Create(new { Decision = "Accept" })
        };
        omittedRequest.Headers.Add("Idempotency-Key", "omitted-key-0001");
        using var omittedResponse = await omittedClient.SendAsync(omittedRequest);
        var omittedData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(omittedResponse);

        using var whitespaceRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            whitespace.DeliveryId,
            "white-key-0001",
            "Reject",
            "   ");
        using var whitespaceResponse = await whitespaceClient.SendAsync(whitespaceRequest);
        var whitespaceData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(whitespaceResponse);

        Assert.Equal(JsonValueKind.Null, omittedData.GetProperty("FeedbackText").ValueKind);
        Assert.Equal(JsonValueKind.Null, whitespaceData.GetProperty("FeedbackText").ValueKind);

        var omittedAfter = await Phase8InteractionTestHelpers.SnapshotAsync(factory, omitted.DeliveryId, omitted.CompanyWalletId, omitted.DoctorWalletId);
        var whitespaceAfter = await Phase8InteractionTestHelpers.SnapshotAsync(factory, whitespace.DeliveryId, whitespace.CompanyWalletId, whitespace.DoctorWalletId);
        Assert.Null(omittedAfter.FeedbackText);
        Assert.Null(omittedAfter.FeedbackCreatedAtUtc);
        Assert.Null(whitespaceAfter.FeedbackText);
        Assert.Null(whitespaceAfter.FeedbackCreatedAtUtc);

        Assert.Null(await FindOperationFeedbackAsync(factory, omitted.DeliveryId));
        Assert.Null(await FindOperationFeedbackAsync(factory, whitespace.DeliveryId));
    }

    [Fact]
    public async Task Interact_NonEmptyFeedback_IsTrimmedStoredLinked_AndFinanciallyNeutral()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var noFeedback = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "feedback-neutral-none");
        var withFeedback = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "feedback-neutral-text");

        using var noFeedbackClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, noFeedback.DoctorUserId);
        using var withFeedbackClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, withFeedback.DoctorUserId);
        using var noFeedbackRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            noFeedback.DeliveryId,
            "neutral-key-0001",
            "Accept");
        using var noFeedbackResponse = await noFeedbackClient.SendAsync(noFeedbackRequest);
        await Phase8InteractionTestHelpers.ReadSuccessDataAsync(noFeedbackResponse);

        using var feedbackRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            withFeedback.DeliveryId,
            "neutral-key-0002",
            "Accept",
            "  useful campaign details  ");
        using var feedbackResponse = await withFeedbackClient.SendAsync(feedbackRequest);
        var feedbackData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(feedbackResponse);

        var noFeedbackAfter = await Phase8InteractionTestHelpers.SnapshotAsync(factory, noFeedback.DeliveryId, noFeedback.CompanyWalletId, noFeedback.DoctorWalletId);
        var withFeedbackAfter = await Phase8InteractionTestHelpers.SnapshotAsync(factory, withFeedback.DeliveryId, withFeedback.CompanyWalletId, withFeedback.DoctorWalletId);

        Assert.Equal("useful campaign details", feedbackData.GetProperty("FeedbackText").GetString());
        Assert.Equal("useful campaign details", withFeedbackAfter.FeedbackText);
        Assert.Equal(withFeedbackAfter.InteractedAtUtc, withFeedbackAfter.FeedbackCreatedAtUtc);
        Assert.Equal("useful campaign details", await FindOperationFeedbackAsync(factory, withFeedback.DeliveryId));
        Assert.Equal(noFeedbackAfter.ChargeAmount, withFeedbackAfter.ChargeAmount);
        Assert.Equal(noFeedbackAfter.EarnAmount, withFeedbackAfter.EarnAmount);
        Assert.Equal(noFeedbackAfter.CompanyReservedBalance, withFeedbackAfter.CompanyReservedBalance);
        Assert.Equal(noFeedbackAfter.DoctorAvailableBalance, withFeedbackAfter.DoctorAvailableBalance);
    }

    [Fact]
    public async Task Interact_FeedbackLengthBoundary_AcceptsOneThousand_RejectsOneThousandOneBeforeMutation()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var accepted = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "feedback-1000");
        var rejected = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "feedback-1001");
        var rejectedBefore = await Phase8InteractionTestHelpers.SnapshotAsync(factory, rejected.DeliveryId, rejected.CompanyWalletId, rejected.DoctorWalletId);

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, accepted.DoctorUserId);
        using var acceptedRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            accepted.DeliveryId,
            "length-key-0001",
            "Accept",
            $" {new string('a', 1000)} ");
        using var acceptedResponse = await client.SendAsync(acceptedRequest);
        var acceptedData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(acceptedResponse);

        Assert.Equal(1000, acceptedData.GetProperty("FeedbackText").GetString()?.Length);
        var acceptedAfter = await Phase8InteractionTestHelpers.SnapshotAsync(factory, accepted.DeliveryId, accepted.CompanyWalletId, accepted.DoctorWalletId);
        Assert.Equal(1000, acceptedAfter.FeedbackText?.Length);
        Assert.NotNull(acceptedAfter.FeedbackCreatedAtUtc);

        using var rejectedRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            rejected.DeliveryId,
            "length-key-0002",
            "Reject",
            $" {new string('b', 1001)} ");
        using var rejectedResponse = await client.SendAsync(rejectedRequest);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(rejectedResponse, HttpStatusCode.BadRequest);

        var rejectedAfter = await Phase8InteractionTestHelpers.SnapshotAsync(factory, rejected.DeliveryId, rejected.CompanyWalletId, rejected.DoctorWalletId);
        Assert.Equal(rejectedBefore, rejectedAfter);
    }

    [Fact]
    public async Task Interact_SameKeySameDecisionDifferentNormalizedFeedback_ConflictsAndPreservesOriginalFeedback()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "feedback-conflict");

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var firstRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "fbconflict-0001",
            "Reject",
            " original feedback ");
        using var firstResponse = await client.SendAsync(firstRequest);
        await Phase8InteractionTestHelpers.ReadSuccessDataAsync(firstResponse);
        var settled = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        using var conflictRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "fbconflict-0001",
            "Reject",
            "changed feedback");
        using var conflictResponse = await client.SendAsync(conflictRequest);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(conflictResponse, HttpStatusCode.Conflict);

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal("original feedback", after.FeedbackText);
        Assert.Equal(settled.Status, after.Status);
        Assert.Equal(settled.ReservationStatus, after.ReservationStatus);
        Assert.Equal(settled.InteractedAtUtc, after.InteractedAtUtc);
        Assert.Equal(settled.CompanyReservedBalance, after.CompanyReservedBalance);
        Assert.Equal(settled.DoctorAvailableBalance, after.DoctorAvailableBalance);
        Assert.Equal(settled.WalletTransactionCount, after.WalletTransactionCount);
        Assert.Equal(settled.WalletLedgerEntryCount, after.WalletLedgerEntryCount);
        Assert.Equal(1, after.ConflictAuditCount);
    }

    private static async Task<string?> FindOperationFeedbackAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string deliveryId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.DeliveryInteractionOperations
            .AsNoTracking()
            .Where(operation => operation.DeliveryId == deliveryId)
            .Select(operation => operation.FeedbackText)
            .SingleAsync();
    }
}
