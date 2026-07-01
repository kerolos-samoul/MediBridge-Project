using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignDraftWorkflowTests
{
    [Fact]
    public async Task Company_CreatesDraftAndUploadsPendingAsset()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);

        using var draftResponse = await client.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Cardiology campaign", Description = "Draft campaign description" });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var campaignId = await ReadDataStringAsync(draftResponse, "campaignId");

        using var assetResponse = await UploadAssetAsync(client, campaignId);
        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
        var assetId = await ReadDataStringAsync(assetResponse, "assetId");

        var campaign = await WalletCampaignWorkflowTestHelpers.FindCampaignAsync(factory, campaignId);
        var asset = await WalletCampaignWorkflowTestHelpers.FindAssetAsync(factory, assetId);
        Assert.NotNull(campaign);
        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.NotNull(asset);
        Assert.Equal(StoredFileOwnerType.Campaign, asset.OwnerType);
        Assert.Equal(campaignId, asset.OwnerId);
        Assert.Equal(StoredFilePurpose.CampaignMedia, asset.Purpose);
        Assert.Equal(StoredFileReviewStatus.Pending, asset.ReviewStatus);
    }

    private static async Task<string> ReadDataStringAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").GetProperty(propertyName).GetString()!;
    }

    private static Task<HttpResponseMessage> UploadAssetAsync(HttpClient client, string campaignId)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "campaign.png");
        return client.PostAsync($"/api/company/campaigns/{campaignId}/assets", form);
    }
}
