using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyCampaignAnalyticsContractTests
{
    [Fact]
    public async Task GetCampaignAnalytics_ReturnsEnvelopeRateMetricShapeAndValidatesDates()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await CompanyCampaignReportingContractSeed.SeedAsync(factory, includeDeliveries: false);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync($"/api/company/campaigns/{seed.CampaignId}/analytics?fromDateEgypt=2026-07-01&toDateEgypt=2026-07-13");
        using var invalid = await client.GetAsync($"/api/company/campaigns/{seed.CampaignId}/analytics?fromDateEgypt=bad");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal(seed.CampaignId, data.GetProperty("CampaignId").GetString());
            Assert.Equal(0, data.GetProperty("DeliveredCount").GetInt32());
            var rate = data.GetProperty("InteractionRate");
            Assert.Equal(0m, rate.GetProperty("Value").GetDecimal());
            Assert.False(rate.GetProperty("HasEligibleRecords").GetBoolean());
        }

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task GetCampaignAnalytics_WithDiscrepantEvidence_Returns409Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await CompanyCampaignReportingContractSeed.SeedAsync(factory, includeDeliveries: true, includeFeedback: true);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync($"/api/company/campaigns/{seed.CampaignId}/analytics?fromDateEgypt=2026-07-01&toDateEgypt=2026-07-13");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 409);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }
}
