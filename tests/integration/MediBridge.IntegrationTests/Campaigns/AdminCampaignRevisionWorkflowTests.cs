using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignRevisionWorkflowTests
{
    [Fact]
    public async Task RevisionRequired_OwnerCanUpdateManageAssetsAndResubmitWithoutWalletMutation()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var nonOwner = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var owner = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        using var otherCompany = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, nonOwner.CompanyUserId);
        await SetDoctorPriceAsync(admin, actors.DoctorProfileId);
        var initialSubmittedAtUtc = DateTime.UtcNow.AddHours(-1);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            initialSubmittedAtUtc,
            createWalletRow: true,
            availableBalance: 500m);
        var initialTargetId = await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(
            factory,
            campaign.CampaignId,
            actors.DoctorProfileId,
            initialSubmittedAtUtc);
        var initialAssetId = await WalletCampaignWorkflowTestHelpers.AddPendingCampaignMediaAsync(factory, campaign.CampaignId);
        await AddSubmissionAttemptAsync(factory, campaign.CampaignId, "revision-initial-submit", initialSubmittedAtUtc);
        var walletBefore = await CaptureWalletStateAsync(factory, campaign.WalletId!);

        using var revision = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "revision-first-review",
            "RevisionRequired",
            "Correct the campaign wording.");
        Assert.Equal(HttpStatusCode.OK, revision.StatusCode);

        using var update = await owner.PutAsJsonAsync(
            $"/api/company/campaigns/{campaign.CampaignId}",
            new
            {
                Title = "Corrected revision title",
                Description = "Corrected revision description.",
                ClinicalResearchInfo = "Corrected clinical reference."
            });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updatedCampaign = await WalletCampaignWorkflowTestHelpers.FindCampaignAsync(factory, campaign.CampaignId);
        Assert.Equal(CampaignStatus.RevisionRequired, updatedCampaign!.Status);
        Assert.Equal(initialSubmittedAtUtc, updatedCampaign.SubmittedAtUtc);

        using var forbiddenUpdate = await otherCompany.PutAsJsonAsync(
            $"/api/company/campaigns/{campaign.CampaignId}",
            new { Title = "Forbidden", Description = "Forbidden update." });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpdate.StatusCode);

        using var replacement = await UploadAsync(
            owner,
            $"/api/company/campaigns/{campaign.CampaignId}/assets/{initialAssetId}/replacement",
            "replacement.png");
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        var replacementId = await ReadDataStringAsync(replacement, "assetId");
        using var deletion = await owner.DeleteAsync($"/api/company/campaigns/{campaign.CampaignId}/assets/{replacementId}");
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        using var upload = await UploadAsync(owner, $"/api/company/campaigns/{campaign.CampaignId}/assets", "corrected.png");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        using var resubmit = await SubmitAsync(owner, campaign.CampaignId, "revision-second-submit");
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        var resubmitData = await ReadDataAsync(resubmit);
        var resubmittedAtUtc = resubmitData.GetProperty("submittedAtUtc").GetDateTime();
        Assert.True(resubmittedAtUtc > initialSubmittedAtUtc);
        Assert.Equal("PendingReview", resubmitData.GetProperty("status").GetString());
        Assert.Equal("EGP", resubmitData.GetProperty("currency").GetString());
        Assert.Equal(50m, resubmitData.GetProperty("estimatedCost").GetDecimal());

        using var pendingUpdate = await owner.PutAsJsonAsync(
            $"/api/company/campaigns/{campaign.CampaignId}",
            new { Title = "Blocked", Description = "Blocked while pending." });
        Assert.Equal(HttpStatusCode.Conflict, pendingUpdate.StatusCode);

        var stateAfterSubmission = await CaptureCampaignStateAsync(factory, campaign.CampaignId);
        Assert.Equal(CampaignStatus.PendingReview, stateAfterSubmission.Status);
        Assert.Equal("Corrected revision title", stateAfterSubmission.Title);
        Assert.Equal("Corrected revision description.", stateAfterSubmission.Description);
        Assert.Equal("Corrected clinical reference.", stateAfterSubmission.ClinicalResearchInfo);
        Assert.Equal(resubmittedAtUtc, stateAfterSubmission.SubmittedAtUtc);
        Assert.DoesNotContain(initialTargetId, stateAfterSubmission.TargetIds, StringComparison.Ordinal);
        Assert.Equal(2, stateAfterSubmission.SubmissionAttemptCount);
        Assert.Equal(1, stateAfterSubmission.ReviewHistoryCount);
        Assert.Equal(walletBefore, await CaptureWalletStateAsync(factory, campaign.WalletId!));

        using var identicalRetry = await SubmitAsync(owner, campaign.CampaignId, "revision-second-submit");
        Assert.Equal(HttpStatusCode.OK, identicalRetry.StatusCode);
        var retryData = await ReadDataAsync(identicalRetry);
        Assert.Equal(resubmittedAtUtc, retryData.GetProperty("submittedAtUtc").GetDateTime());
        Assert.Equal(50m, retryData.GetProperty("estimatedCost").GetDecimal());
        Assert.Equal(stateAfterSubmission, await CaptureCampaignStateAsync(factory, campaign.CampaignId));

        using var differentKey = await SubmitAsync(owner, campaign.CampaignId, "revision-conflicting-submit");
        Assert.Equal(HttpStatusCode.Conflict, differentKey.StatusCode);
        Assert.Equal(stateAfterSubmission, await CaptureCampaignStateAsync(factory, campaign.CampaignId));
        Assert.Equal(walletBefore, await CaptureWalletStateAsync(factory, campaign.WalletId!));
    }

    [Fact]
    public async Task HistoricalSubmissionKey_AfterAnotherRevision_ReturnsConflictWithoutMutation()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        await SetDoctorPriceAsync(admin, actors.DoctorProfileId);
        var firstSubmittedAtUtc = DateTime.UtcNow.AddHours(-2);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            firstSubmittedAtUtc,
            createWalletRow: true,
            availableBalance: 500m);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddPendingCampaignMediaAsync(factory, campaign.CampaignId);
        await AddSubmissionAttemptAsync(factory, campaign.CampaignId, "historical-first-submit", firstSubmittedAtUtc);

        using var firstRevision = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "historical-first-review",
            "RevisionRequired",
            "First revision.");
        Assert.Equal(HttpStatusCode.OK, firstRevision.StatusCode);
        using var resubmission = await SubmitAsync(company, campaign.CampaignId, "historical-second-submit");
        Assert.Equal(HttpStatusCode.OK, resubmission.StatusCode);
        using var secondRevision = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "historical-second-review",
            "RevisionRequired",
            "Second revision.");
        Assert.Equal(HttpStatusCode.OK, secondRevision.StatusCode);
        var beforeConflict = await CaptureCampaignStateAsync(factory, campaign.CampaignId);
        var walletBefore = await CaptureWalletStateAsync(factory, campaign.WalletId!);

        using var conflict = await SubmitAsync(company, campaign.CampaignId, "historical-first-submit");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(CampaignStatus.RevisionRequired, beforeConflict.Status);
        Assert.Equal(2, beforeConflict.SubmissionAttemptCount);
        Assert.Equal(2, beforeConflict.ReviewHistoryCount);
        Assert.Equal(beforeConflict, await CaptureCampaignStateAsync(factory, campaign.CampaignId));
        Assert.Equal(walletBefore, await CaptureWalletStateAsync(factory, campaign.WalletId!));
    }

    private static async Task SetDoctorPriceAsync(HttpClient admin, string doctorId)
    {
        using var response = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{doctorId}/price",
            new { PricePerMessage = 50m, Reason = "Revision workflow price" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AddSubmissionAttemptAsync(
        WebAppFactory factory,
        string campaignId,
        string idempotencyKey,
        DateTime submittedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.CampaignSubmissionAttempts.AddAsync(new CampaignSubmissionAttempt
        {
            CampaignId = campaignId,
            IdempotencyKey = idempotencyKey,
            SubmittedAtUtc = submittedAtUtc,
            TargetCount = 1,
            EstimatedCost = 50m,
            Currency = "EGP",
            CreatedAtUtc = submittedAtUtc
        });
        await context.SaveChangesAsync();
    }

    private static async Task<CampaignState> CaptureCampaignStateAsync(WebAppFactory factory, string campaignId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaignId);
        return new CampaignState(
            campaign.Title,
            campaign.Description,
            campaign.ClinicalResearchInfo,
            campaign.Status,
            campaign.SubmittedAtUtc,
            string.Join(",", await context.CampaignTargets.AsNoTracking()
                .Where(item => item.CampaignId == campaignId)
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync()),
            await context.CampaignSubmissionAttempts.CountAsync(item => item.CampaignId == campaignId),
            await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaignId));
    }

    private static async Task<WalletState> CaptureWalletStateAsync(WebAppFactory factory, string walletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId);
        return new WalletState(
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

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").Clone();
    }

    private static async Task<string> ReadDataStringAsync(HttpResponseMessage response, string propertyName)
        => (await ReadDataAsync(response)).GetProperty(propertyName).GetString()!;

    private sealed record CampaignState(
        string Title,
        string Description,
        string? ClinicalResearchInfo,
        CampaignStatus Status,
        DateTime? SubmittedAtUtc,
        string TargetIds,
        int SubmissionAttemptCount,
        int ReviewHistoryCount);

    private sealed record WalletState(
        decimal AvailableBalance,
        decimal ReservedBalance,
        int WalletTransactionCount,
        int WalletLedgerCount);
}
