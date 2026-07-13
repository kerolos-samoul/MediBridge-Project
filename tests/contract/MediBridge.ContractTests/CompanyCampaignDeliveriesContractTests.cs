using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyCampaignDeliveriesContractTests
{
    [Fact]
    public async Task GetCampaignDeliveries_ReturnsEnvelopeSchemaAndRejectsInvalidQuery()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await CompanyCampaignReportingContractSeed.SeedAsync(factory, includeDeliveries: true);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync($"/api/company/campaigns/{seed.CampaignId}/deliveries?fromDateEgypt=2026-07-01&toDateEgypt=2026-07-13&PageNumber=1&PageSize=20");
        using var invalid = await client.GetAsync($"/api/company/campaigns/{seed.CampaignId}/deliveries?PageSize=101");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.True(data.TryGetProperty("Page", out _));
            var row = data.GetProperty("Items")[0];
            Assert.True(row.TryGetProperty("DeliveryId", out _));
            Assert.True(row.TryGetProperty("PublicDoctorId", out _));
            Assert.True(row.TryGetProperty("DoctorSpecialization", out _));
            Assert.True(row.TryGetProperty("DoctorExperienceBand", out _));
            Assert.True(row.TryGetProperty("DoctorLocation", out _));
            Assert.False(row.TryGetProperty("DoctorName", out _));
            Assert.False(row.TryGetProperty("DoctorEmail", out _));
            Assert.False(row.TryGetProperty("WalletId", out _));
            Assert.False(row.TryGetProperty("IdempotencyKey", out _));
        }

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
