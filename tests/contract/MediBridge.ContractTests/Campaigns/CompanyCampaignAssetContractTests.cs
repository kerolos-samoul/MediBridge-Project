using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Campaigns;

public sealed class CompanyCampaignAssetContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task CompanyCampaignAsset_WithValidFile_ReturnsPendingAssetEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);
        var campaignId = await CreateDraftAsync(client);

        using var response = await UploadAssetAsync(client, campaignId, "campaign.png", new byte[] { 1, 2, 3 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 201, "Created");
        var data = envelope.GetProperty("Data");
        Assert.Equal(campaignId, data.GetProperty("campaignId").GetString());
        Assert.Equal("Pending", data.GetProperty("reviewStatus").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("assetId").GetString()));
    }

    [Fact]
    public async Task CompanyCampaignAsset_WithEmptyFile_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);
        var campaignId = await CreateDraftAsync(client);

        using var response = await UploadAssetAsync(client, campaignId, "campaign.png", Array.Empty<byte>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task CompanyCampaignAsset_FromAnotherCompany_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await CreateApprovedActorsAsync(factory);
        var other = await CreateApprovedActorsAsync(factory);
        using var ownerClient = CreateCompanyClient(factory, owner.CompanyUserId);
        using var otherClient = CreateCompanyClient(factory, other.CompanyUserId);
        var campaignId = await CreateDraftAsync(ownerClient);

        using var response = await UploadAssetAsync(otherClient, campaignId, "campaign.png", new byte[] { 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task CompanyCampaignAsset_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var companyClient = CreateCompanyClient(factory, ids.CompanyUserId);
        using var doctorClient = CreateDoctorClient(factory, ids.DoctorUserId);
        var campaignId = await CreateDraftAsync(companyClient);

        using var response = await UploadAssetAsync(doctorClient, campaignId, "campaign.png", new byte[] { 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task CompanyCampaignAsset_ForInvisibleCampaign_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await UploadAssetAsync(client, Guid.NewGuid().ToString("N"), "campaign.png", new byte[] { 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    private static async Task<string> CreateDraftAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            WalletCampaignWorkflowRoutes.CompanyCampaigns,
            new { Title = "Draft", Description = "Description" });
        var envelope = await AssertEnvelopeAsync(response, 201, "Created");
        return envelope.GetProperty("Data").GetProperty("campaignId").GetString()!;
    }

    private static Task<HttpResponseMessage> UploadAssetAsync(HttpClient client, string campaignId, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        return client.PostAsync(
            WalletCampaignWorkflowRoutes.CompanyCampaignAssets.Replace("{campaignId}", campaignId, StringComparison.Ordinal),
            form);
    }
}
