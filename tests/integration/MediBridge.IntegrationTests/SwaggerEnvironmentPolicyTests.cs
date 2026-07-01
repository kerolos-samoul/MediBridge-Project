using System.Net;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MediBridge.IntegrationTests;

public class SwaggerEnvironmentPolicyTests
{
    [Fact]
    public async Task SwaggerNotExposedInProduction()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

        var res = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task SwaggerExposedInDevelopment()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        var res = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Swagger_ExposesOnlyTheSupportedDraftCampaignSubmissionWorkflow()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.False(paths.GetProperty("/api/company/campaigns").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/company/campaigns/drafts").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/campaigns/{campaignId}/files").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/submit").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/company/wallet/mock-checkout").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/admin/doctors/{doctorId}/price").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/target-preview").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/queue-summary").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/assets").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/assets/{assetId}/replacement").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/assets/{assetId}").TryGetProperty("delete", out _));
        Assert.True(paths.GetProperty("/api/files/{fileId}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/admin/campaign-assets/{assetId}/review").TryGetProperty("post", out _));
    }
}
