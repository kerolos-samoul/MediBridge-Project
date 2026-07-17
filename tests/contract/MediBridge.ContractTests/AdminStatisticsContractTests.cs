using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AdminStatisticsContractTests
{
    [Fact]
    public async Task Statistics_WithAdminToken_ReturnsStandardEnvelopeSchemaAndDateParameters()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await WithdrawalContractTestData.SeedAdminAsync(factory.Services);
        using var client = CreateAdminClient(factory, adminUserId);

        using var response = await client.GetAsync($"{AdminToolsRoutes.AdminStatistics}?fromDateEgypt=2026-07-01&toDateEgypt=2026-07-13");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal("2026-07-01", data.GetProperty("fromDateEgypt").GetString());
        Assert.Equal("2026-07-13", data.GetProperty("toDateEgypt").GetString());
        Assert.Equal(JsonValueKind.Object, data.GetProperty("accountCounts").ValueKind);
        Assert.Equal(JsonValueKind.Object, data.GetProperty("reviewCounts").ValueKind);
        Assert.Equal(JsonValueKind.Object, data.GetProperty("campaignCounts").ValueKind);
        Assert.Equal(JsonValueKind.Object, data.GetProperty("deliveryOutcomeCounts").ValueKind);
        Assert.Equal(JsonValueKind.Object, data.GetProperty("interactionOutcomeCounts").ValueKind);
        Assert.Equal(JsonValueKind.Object, data.GetProperty("withdrawalStatusCounts").ValueKind);
        Assert.Equal(JsonValueKind.Object, data.GetProperty("pricingPolicySummary").ValueKind);
        Assert.Equal(JsonValueKind.Object, data.GetProperty("enforcementActionCounts").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("withheldFinancialScopes").ValueKind);
    }

    [Theory]
    [InlineData("?fromDateEgypt=not-a-date&toDateEgypt=2026-07-13")]
    [InlineData("?fromDateEgypt=2026-07-14&toDateEgypt=2026-07-13")]
    [InlineData("?fromDateEgypt=2026-04-01&toDateEgypt=2026-07-13")]
    public async Task Statistics_WithInvalidDateRange_ReturnsBadRequestEnvelope(string query)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await WithdrawalContractTestData.SeedAdminAsync(factory.Services);
        using var client = CreateAdminClient(factory, adminUserId);

        using var response = await client.GetAsync(AdminToolsRoutes.AdminStatistics + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task Statistics_RequiresAdminAuthorization()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.GetAsync(AdminToolsRoutes.AdminStatistics);
        using var doctor = factory.CreateClient();
        doctor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor"));
        using var forbidden = await doctor.GetAsync(AdminToolsRoutes.AdminStatistics);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var unauthorizedDocument = JsonDocument.Parse(await unauthorized.Content.ReadAsStringAsync());
        using var forbiddenDocument = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync());
        Phase5ContractTestHelpers.AssertEnvelope(unauthorizedDocument.RootElement, 401);
        Phase5ContractTestHelpers.AssertEnvelope(forbiddenDocument.RootElement, 403);
    }

    private static HttpClient CreateAdminClient(ContractWebAppFactory factory, string adminUserId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Admin", adminUserId));
        return client;
    }
}
