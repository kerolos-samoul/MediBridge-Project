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
    public async Task Swagger_DocumentsPhase10CompanyReportingRoutes()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/company/campaigns").TryGetProperty("get", out var summaries));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/deliveries").TryGetProperty("get", out var deliveries));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/feedback").TryGetProperty("get", out var feedback));
        Assert.True(paths.GetProperty("/api/company/campaigns/{campaignId}/analytics").TryGetProperty("get", out var analytics));

        AssertResponses(summaries, "200", "400", "401", "403", "409");
        AssertResponses(deliveries, "200", "400", "401", "403", "404");
        AssertResponses(feedback, "200", "400", "401", "403", "404");
        AssertResponses(analytics, "200", "400", "401", "403", "404", "409");
    }

    [Fact]
    public async Task Swagger_DocumentsTheSecuredPhase7AndPhase8DoctorRoutesAndContractedResponses()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(
            [
                "/api/doctor/messages/today",
                "/api/doctor/messages/{deliveryId}/assets/{fileId}/access",
                "/api/doctor/messages/{deliveryId}/interact",
                "/api/doctor/messages/{deliveryId}/read"
            ],
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
        var read = paths.GetProperty("/api/doctor/messages/{deliveryId}/read").GetProperty("put");
        AssertResponses(read, "200", "401", "403", "404", "429");
        var interact = paths.GetProperty("/api/doctor/messages/{deliveryId}/interact").GetProperty("post");
        AssertResponses(interact, "200", "400", "401", "403", "404", "409", "429", "503");

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var inboxSchema = schemas.EnumerateObject().Single(schema => schema.Name.EndsWith("TodayInboxDto", StringComparison.Ordinal)).Value;
        var nextCursor = inboxSchema.GetProperty("properties").GetProperty("NextCursor");
        Assert.True(nextCursor.GetProperty("nullable").GetBoolean());

        Assert.False(paths.TryGetProperty("/hangfire", out _));
        Assert.Equal(
            [
                "/api/admin/delivery-jobs/run-expiry",
                "/api/admin/delivery-jobs/run-injector",
                "/api/admin/delivery-jobs/status"
            ],
            paths.EnumerateObject()
                .Select(path => path.Name)
                .Where(path => path.Contains("delivery-jobs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());
        Assert.DoesNotContain(paths.EnumerateObject(), path =>
            path.Name.Contains("jobs/retry", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path.Name, "/jobs", StringComparison.OrdinalIgnoreCase));

        var authorize = typeof(DoctorMessagesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.Phase7DoctorMessagesRead, authorize?.Policy);
        Assert.Null(typeof(DoctorMessagesController).GetCustomAttribute<EnableRateLimitingAttribute>());
        Assert.Equal(RateLimitPolicyNames.Phase7DoctorMessagesRead, typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.GetToday))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal(RateLimitPolicyNames.Phase7DoctorMessagesRead, typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.GetAssetAccess))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal(RateLimitPolicyNames.DoctorInteraction, typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.MarkRead))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal(RateLimitPolicyNames.DoctorInteraction, typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.Interact))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);

        var adminJobsAuthorize = typeof(AdminDeliveryJobsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.AdminOnly, adminJobsAuthorize?.Policy);
        var adminJobsRateLimit = typeof(AdminDeliveryJobsController).GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal(RateLimitPolicyNames.Envelope, adminJobsRateLimit?.PolicyName);
    }

    [Fact]
    public async Task Swagger_DocumentsTheSecuredPhase9AdminEnforcementRoutes()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        var violations = paths.GetProperty("/api/admin/violations").GetProperty("get");
        var doctorStatus = paths.GetProperty("/api/admin/doctors/{doctorId}/status").GetProperty("put");

        AssertOperationReferencesBearer(violations);
        AssertOperationReferencesBearer(doctorStatus);
        AssertResponses(violations, "200", "400", "401", "403", "404", "409");
        AssertResponses(doctorStatus, "200", "400", "401", "403", "404", "409");

        var authorize = typeof(AdminActivityEnforcementController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.AdminOnly, authorize?.Policy);
        var rateLimit = typeof(AdminActivityEnforcementController).GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal(RateLimitPolicyNames.Envelope, rateLimit?.PolicyName);
    }

    [Fact]
    public async Task Swagger_DocumentsBearerSecurityAndRequiredIdempotencyForActualReplaySafeEndpoints()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        Assert.True(schemes.TryGetProperty("Bearer", out var bearer));
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        var paths = root.GetProperty("paths");
        AssertOperationReferencesBearer(paths.GetProperty("/api/admin/campaigns/{campaignId}/review").GetProperty("post"));
        AssertRequiredIdempotencyKey(paths, "/api/company/campaigns/{campaignId}/submit", "post");
        AssertRequiredIdempotencyKey(paths, "/api/admin/campaigns/{campaignId}/review", "post");
        AssertRequiredIdempotencyKey(paths, "/api/company/wallet/topup", "post");
        AssertRequiredIdempotencyKey(paths, "/api/company/wallet/mock-checkout", "post");
        AssertRequiredIdempotencyKey(paths, "/api/doctor/messages/{deliveryId}/interact", "post");
        AssertNoIdempotencyKey(paths, "/api/company/campaigns/drafts", "post");
        AssertNoIdempotencyKey(paths, "/api/auth/login", "post");
    }

    private static void AssertResponses(JsonElement operation, params string[] expectedStatusCodes)
    {
        var responses = operation.GetProperty("responses");
        foreach (var statusCode in expectedStatusCodes)
        {
            Assert.True(responses.TryGetProperty(statusCode, out _), $"Response {statusCode} was not documented.");
        }
    }

    private static void AssertOperationReferencesBearer(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Contains(security.EnumerateArray(), requirement =>
            requirement.TryGetProperty("Bearer", out var scopes)
            && scopes.ValueKind == JsonValueKind.Array
            && !scopes.EnumerateArray().Any());
    }

    private static void AssertRequiredIdempotencyKey(JsonElement paths, string path, string method)
    {
        Assert.True(
            HasRequiredIdempotencyKey(paths.GetProperty(path).GetProperty(method)),
            $"Expected required Idempotency-Key on {method.ToUpperInvariant()} {path}.");
    }

    private static void AssertNoIdempotencyKey(JsonElement paths, string path, string method)
    {
        var operation = paths.GetProperty(path).GetProperty(method);
        if (!operation.TryGetProperty("parameters", out var parameters))
        {
            return;
        }

        Assert.DoesNotContain(parameters.EnumerateArray(), parameter =>
            string.Equals(parameter.GetProperty("name").GetString(), "Idempotency-Key", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parameter.GetProperty("in").GetString(), "header", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasRequiredIdempotencyKey(JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out var parameters))
        {
            return false;
        }

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (string.Equals(parameter.GetProperty("name").GetString(), "Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parameter.GetProperty("in").GetString(), "header", StringComparison.OrdinalIgnoreCase)
                && parameter.TryGetProperty("required", out var required)
                && required.GetBoolean())
            {
                return true;
            }
        }

        return false;
    }
}
