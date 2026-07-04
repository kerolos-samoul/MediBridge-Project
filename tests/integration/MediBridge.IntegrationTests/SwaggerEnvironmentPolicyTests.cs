using System.Net;
using System.Reflection;
using System.Text.Json;
using MediBridge.APIs.Config;
using MediBridge.APIs.Controllers;
using MediBridge.APIs.Security;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
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

    [Fact]
    public async Task Swagger_DocumentsOnlyTheSecuredPhase7DoctorRoutesAndContractedResponses()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(
            ["/api/doctor/messages/today", "/api/doctor/messages/{deliveryId}/assets/{fileId}/access"],
            paths.EnumerateObject()
                .Select(path => path.Name)
                .Where(path => path.StartsWith("/api/doctor/messages", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());

        var inbox = paths.GetProperty("/api/doctor/messages/today").GetProperty("get");
        Assert.Equal(
            ["PageSize", "Cursor"],
            inbox.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString()!)
                .ToArray());
        AssertResponses(inbox, "200", "400", "401", "403", "429");

        var asset = paths.GetProperty("/api/doctor/messages/{deliveryId}/assets/{fileId}/access").GetProperty("get");
        AssertResponses(asset, "200", "401", "403", "404", "429", "503");

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var inboxSchema = schemas.EnumerateObject().Single(schema => schema.Name.EndsWith("TodayInboxDto", StringComparison.Ordinal)).Value;
        var nextCursor = inboxSchema.GetProperty("properties").GetProperty("NextCursor");
        Assert.True(nextCursor.GetProperty("nullable").GetBoolean());

        Assert.False(paths.TryGetProperty("/hangfire", out _));
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.Contains("job", StringComparison.OrdinalIgnoreCase));

        var authorize = typeof(DoctorMessagesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.Phase7DoctorMessagesRead, authorize?.Policy);
        var rateLimit = typeof(DoctorMessagesController).GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal(RateLimitPolicyNames.Phase7DoctorMessagesRead, rateLimit?.PolicyName);
    }

    private static void AssertResponses(JsonElement operation, params string[] expectedStatusCodes)
    {
        var responses = operation.GetProperty("responses");
        foreach (var statusCode in expectedStatusCodes)
        {
            Assert.True(responses.TryGetProperty(statusCode, out _), $"Response {statusCode} was not documented.");
        }
    }
}
