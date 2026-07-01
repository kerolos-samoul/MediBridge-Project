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

public sealed class AdminCampaignRejectionFinalityTests
{
    [Fact]
    public async Task RejectedCampaign_DeniesEveryCompanyMutationAndFurtherAdminApproval()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var submittedAtUtc = DateTime.UtcNow.AddMinutes(-15);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            submittedAtUtc,
            createWalletRow: true,
            availableBalance: 500m);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        var assetId = await WalletCampaignWorkflowTestHelpers.AddPendingCampaignMediaAsync(factory, campaign.CampaignId);
        await AddSubmissionAttemptAsync(factory, campaign.CampaignId, submittedAtUtc);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);

        using var rejection = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "rejection-finality-review",
            "Rejected",
            "The claims cannot be approved.");
        Assert.Equal(HttpStatusCode.OK, rejection.StatusCode);
        var rejectedState = await CaptureStateAsync(factory, campaign.CampaignId, campaign.WalletId!);

        using var update = await company.PutAsJsonAsync(
            $"/api/company/campaigns/{campaign.CampaignId}",
            new { Title = "Changed title", Description = "Changed description." });
        using var upload = await UploadAsync(company, $"/api/company/campaigns/{campaign.CampaignId}/assets", "new.png");
        using var replacement = await UploadAsync(
            company,
            $"/api/company/campaigns/{campaign.CampaignId}/assets/{assetId}/replacement",
            "replacement.png");
        using var delete = await company.DeleteAsync($"/api/company/campaigns/{campaign.CampaignId}/assets/{assetId}");
        using var submit = await SubmitAsync(company, campaign.CampaignId, "rejection-finality-submit");
        using var approval = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "rejection-finality-approval",
            "Approved",
            null);

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replacement.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, approval.StatusCode);
        Assert.Equal(rejectedState, await CaptureStateAsync(factory, campaign.CampaignId, campaign.WalletId!));
    }

    private static async Task AddSubmissionAttemptAsync(WebAppFactory factory, string campaignId, DateTime submittedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.CampaignSubmissionAttempts.AddAsync(new CampaignSubmissionAttempt
        {
            CampaignId = campaignId,
            IdempotencyKey = "rejection-finality-initial",
            SubmittedAtUtc = submittedAtUtc,
            TargetCount = 1,
            EstimatedCost = 50m,
            Currency = "EGP",
            CreatedAtUtc = submittedAtUtc
        });
        await context.SaveChangesAsync();
    }

    private static async Task<RejectedCampaignState> CaptureStateAsync(
        WebAppFactory factory,
        string campaignId,
        string walletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaignId);
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId);
        return new RejectedCampaignState(
            campaign.Title,
            campaign.Description,
            campaign.ClinicalResearchInfo,
            campaign.Status,
            campaign.SubmittedAtUtc,
            campaign.UpdatedAtUtc,
            await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaignId),
            await context.StoredFiles.CountAsync(item => item.OwnerId == campaignId),
            await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaignId),
            wallet.AvailableBalance,
            wallet.ReservedBalance,
            await context.WalletTransactions.CountAsync(item => item.WalletId == walletId),
            await context.WalletLedgerEntries.CountAsync(item => item.WalletId == walletId));
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

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string campaignId, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{campaignId}/submit");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string route, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(content, "file", fileName);
        return await client.PostAsync(route, form);
    }

    private sealed record RejectedCampaignState(
        string Title,
        string Description,
        string? ClinicalResearchInfo,
        CampaignStatus Status,
        DateTime? SubmittedAtUtc,
        DateTime? UpdatedAtUtc,
        int HistoryCount,
        int FileCount,
        int QueueCount,
        decimal AvailableBalance,
        decimal ReservedBalance,
        int WalletTransactionCount,
        int WalletLedgerCount);
}
