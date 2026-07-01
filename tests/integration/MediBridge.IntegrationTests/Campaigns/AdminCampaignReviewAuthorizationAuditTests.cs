using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignReviewAuditAuthorizationTests
{
    [Fact]
    public async Task AnonymousAndNonAdminPolicyDenials_ReturnEnvelopesWithoutMutationOrAudit()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5),
            createWalletRow: true,
            availableBalance: 250m);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        var before = await CaptureStateAsync(factory, campaign.CampaignId, campaign.WalletId!);
        using var anonymous = factory.CreateClient();
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);

        using var anonymousResponse = await ReviewAsync(anonymous, campaign.CampaignId, "authorization-anonymous");
        using var companyResponse = await ReviewAsync(company, campaign.CampaignId, "authorization-company");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, companyResponse.StatusCode);
        await AssertEnvelopeAsync(anonymousResponse, 401);
        await AssertEnvelopeAsync(companyResponse, 403);
        Assert.Equal(before, await CaptureStateAsync(factory, campaign.CampaignId, campaign.WalletId!));
    }

    private static async Task<ModerationState> CaptureStateAsync(WebAppFactory factory, string campaignId, string walletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaignId);
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId);
        return new ModerationState(
            campaign.Status,
            campaign.SubmittedAtUtc,
            campaign.UpdatedAtUtc,
            await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaignId),
            await context.AuditEvents.CountAsync(item => item.TargetType == AuditTargetType.Campaign && item.TargetId == campaignId),
            await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaignId),
            await context.StoredFiles.CountAsync(item => item.OwnerId == campaignId),
            wallet.AvailableBalance,
            wallet.ReservedBalance,
            await context.WalletTransactions.CountAsync(item => item.WalletId == walletId),
            await context.WalletLedgerEntries.CountAsync(item => item.WalletId == walletId));
    }

    private static async Task<HttpResponseMessage> ReviewAsync(HttpClient client, string campaignId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = "Approved", Reason = (string?)null })
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task AssertEnvelopeAsync(HttpResponseMessage response, int expectedCode)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("Code").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("Message").GetString()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private sealed record ModerationState(
        CampaignStatus Status,
        DateTime? SubmittedAtUtc,
        DateTime? UpdatedAtUtc,
        int HistoryCount,
        int AuditCount,
        int QueueCount,
        int FileCount,
        decimal AvailableBalance,
        decimal ReservedBalance,
        int WalletTransactionCount,
        int WalletLedgerCount);
}
