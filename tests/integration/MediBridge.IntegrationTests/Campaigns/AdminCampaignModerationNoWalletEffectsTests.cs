using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignModerationNoWalletEffectsTests
{
    [Theory]
    [InlineData("Approved", null, CampaignStatus.Approved, 1)]
    [InlineData("Rejected", "The campaign is not suitable.", CampaignStatus.Rejected, 0)]
    [InlineData("RevisionRequired", "Please revise the campaign.", CampaignStatus.RevisionRequired, 0)]
    public async Task ModerationAfterInitialSubmission_DoesNotMutateWalletDeliveryOrPayoutState(
        string decision,
        string? reason,
        CampaignStatus expectedStatus,
        int expectedQueueCount)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var submittedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            submittedAtUtc,
            createWalletRow: true,
            availableBalance: 500m);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        await AddSubmissionAttemptAsync(factory, campaign.CampaignId, "initial-submission-attempt", submittedAtUtc);
        var before = await CaptureFinancialStateAsync(factory, campaign.CampaignId, campaign.WalletId!);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(admin, campaign.CampaignId, $"no-wallet-{decision}", decision, reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await CaptureFinancialStateAsync(factory, campaign.CampaignId, campaign.WalletId!);
        Assert.Equal(before, after);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(expectedStatus, (await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaign.CampaignId)).Status);
        Assert.Equal(expectedQueueCount, await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaign.CampaignId));
        Assert.Equal(1, await context.CampaignSubmissionAttempts.CountAsync(item => item.CampaignId == campaign.CampaignId));
    }

    [Fact]
    public async Task ModerationAfterRevisionRequiredResubmission_PreservesAttemptsHistoryAndFinancialState()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var firstSubmittedAtUtc = DateTime.UtcNow.AddDays(-1);
        var resubmittedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            resubmittedAtUtc,
            createWalletRow: true,
            availableBalance: 500m);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        await SeedRevisionResubmissionEvidenceAsync(
            factory,
            campaign.CampaignId,
            actors.AdminUserId,
            firstSubmittedAtUtc,
            resubmittedAtUtc);
        var before = await CaptureFinancialStateAsync(factory, campaign.CampaignId, campaign.WalletId!);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(admin, campaign.CampaignId, "resubmission-approval", "Approved", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await CaptureFinancialStateAsync(factory, campaign.CampaignId, campaign.WalletId!);
        Assert.Equal(before, after);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(2, await context.CampaignSubmissionAttempts.CountAsync(item => item.CampaignId == campaign.CampaignId));
        Assert.Equal(2, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaign.CampaignId));
        Assert.Equal(CampaignStatus.Approved, (await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaign.CampaignId)).Status);
    }

    private static async Task SeedRevisionResubmissionEvidenceAsync(
        WebAppFactory factory,
        string campaignId,
        string adminUserId,
        DateTime firstSubmittedAtUtc,
        DateTime resubmittedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.CampaignSubmissionAttempts.AddRangeAsync(
            CreateAttempt(campaignId, "initial-attempt", firstSubmittedAtUtc),
            CreateAttempt(campaignId, "revision-resubmission-attempt", resubmittedAtUtc));
        await context.CampaignReviewHistories.AddAsync(new CampaignReviewHistory
        {
            CampaignId = campaignId,
            AdminUserId = adminUserId,
            Decision = CampaignReviewDecision.RevisionRequired,
            IdempotencyKey = "prior-revision-decision",
            Reason = "Please revise.",
            PriorStatus = CampaignStatus.PendingReview,
            ResultingStatus = CampaignStatus.RevisionRequired,
            CreatedAtUtc = firstSubmittedAtUtc.AddHours(1)
        });
        await context.SaveChangesAsync();
    }

    private static async Task AddSubmissionAttemptAsync(
        WebAppFactory factory,
        string campaignId,
        string idempotencyKey,
        DateTime submittedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.CampaignSubmissionAttempts.AddAsync(CreateAttempt(campaignId, idempotencyKey, submittedAtUtc));
        await context.SaveChangesAsync();
    }

    private static CampaignSubmissionAttempt CreateAttempt(string campaignId, string idempotencyKey, DateTime submittedAtUtc)
        => new()
        {
            CampaignId = campaignId,
            IdempotencyKey = idempotencyKey,
            SubmittedAtUtc = submittedAtUtc,
            TargetCount = 1,
            EstimatedCost = 50m,
            Currency = "EGP",
            CreatedAtUtc = submittedAtUtc
        };

    private static async Task<FinancialState> CaptureFinancialStateAsync(
        WebAppFactory factory,
        string campaignId,
        string walletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId);
        return new FinancialState(
            wallet.AvailableBalance,
            wallet.ReservedBalance,
            await context.WalletTransactions.CountAsync(item => item.WalletId == walletId),
            await context.WalletLedgerEntries.CountAsync(item => item.WalletId == walletId),
            await context.DoctorAdDeliveries.CountAsync(item => item.CampaignId == campaignId),
            await context.WithdrawalRequests.CountAsync(),
            await context.MockPaymentTransactions.CountAsync());
    }

    private static async Task<HttpResponseMessage> ReviewAsync(
        HttpClient client,
        string campaignId,
        string idempotencyKey,
        string decision,
        string? reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = decision, Reason = reason })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private sealed record FinancialState(
        decimal AvailableBalance,
        decimal ReservedBalance,
        int WalletTransactionCount,
        int WalletLedgerCount,
        int DeliveryCount,
        int WithdrawalCount,
        int PaymentCount);
}
