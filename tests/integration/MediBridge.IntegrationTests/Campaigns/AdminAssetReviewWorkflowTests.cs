using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignAssetReviewWorkflowTests
{
    [Fact]
    public async Task AdminAssetApproval_AllowsCampaignSubmission()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);

        using var price = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{actors.DoctorProfileId}/price",
            new { PricePerMessage = 50m, Reason = "Campaign review test" });
        Assert.Equal(HttpStatusCode.OK, price.StatusCode);

        using var topUpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = JsonContent.Create(new { Amount = 500m, Currency = "EGP" })
        };
        topUpRequest.Headers.Add("Idempotency-Key", "asset-review-topup-001");
        using var topUp = await company.SendAsync(topUpRequest);
        Assert.Equal(HttpStatusCode.OK, topUp.StatusCode);

        using var draft = await company.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Reviewed campaign", Description = "Asset approval enables submission." });
        var campaignId = await ReadDataStringAsync(draft, "campaignId");
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "campaign.png");
        using var upload = await company.PostAsync($"/api/company/campaigns/{campaignId}/assets", form);
        var assetId = await ReadDataStringAsync(upload, "assetId");

        using var review = await admin.PostAsJsonAsync(
            $"/api/admin/campaign-assets/{assetId}/review",
            new { Decision = "Approved", Reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{campaignId}/submit");
        submitRequest.Headers.Add("Idempotency-Key", "asset-review-submit-001");
        var submittedAfterUtc = DateTime.UtcNow.AddSeconds(-1);
        using var submit = await company.SendAsync(submitRequest);

        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var submittedAtUtc = await ReadDataDateTimeAsync(submit, "submittedAtUtc");
        Assert.True(submittedAtUtc >= submittedAfterUtc);
        var campaign = await WalletCampaignWorkflowTestHelpers.FindCampaignAsync(factory, campaignId);
        var asset = await WalletCampaignWorkflowTestHelpers.FindAssetAsync(factory, assetId);
        Assert.Equal(CampaignStatus.PendingReview, campaign!.Status);
        Assert.Equal(submittedAtUtc, campaign.SubmittedAtUtc);
        Assert.Equal(StoredFileReviewStatus.Approved, asset!.ReviewStatus);
        Assert.Equal(actors.AdminUserId, asset.ReviewedByAdminId);
        Assert.NotNull(asset.ReviewedAtUtc);

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{campaignId}/submit");
        replayRequest.Headers.Add("Idempotency-Key", "asset-review-submit-001");
        using var replay = await company.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(submittedAtUtc, await ReadDataDateTimeAsync(replay, "submittedAtUtc"));
    }

    private static async Task<string> ReadDataStringAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").GetProperty(propertyName).GetString()!;
    }

    private static async Task<DateTime> ReadDataDateTimeAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").GetProperty(propertyName).GetDateTime();
    }
}
