using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignConcurrentReviewTests
{
    [Fact]
    public async Task ConcurrentConflictingReviews_CommitExactlyOneDecision()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-2),
            createWalletRow: true,
            availableBalance: 250m);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        using var firstAdmin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var secondAdmin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        var approveTask = ReviewAsync(firstAdmin, campaign.CampaignId, "concurrent-review-approve", "Approved", null);
        var rejectTask = ReviewAsync(secondAdmin, campaign.CampaignId, "concurrent-review-reject", "Rejected", "Conflicting concurrent decision.");
        var responses = await Task.WhenAll(approveTask, rejectTask);
        using var approve = responses[0];
        using var reject = responses[1];

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var persistedCampaign = await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaign.CampaignId);
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == campaign.WalletId);
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaign.CampaignId));
        Assert.Equal(250m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
        Assert.Equal(0, await context.WalletTransactions.CountAsync(item => item.WalletId == wallet.Id));
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync(item => item.WalletId == wallet.Id));
        if (persistedCampaign.Status == CampaignStatus.Approved)
        {
            Assert.Equal(1, await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaign.CampaignId && item.Status == QueueItemStatus.Queued));
        }
        else
        {
            Assert.Equal(CampaignStatus.Rejected, persistedCampaign.Status);
            Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaign.CampaignId));
        }
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
}
