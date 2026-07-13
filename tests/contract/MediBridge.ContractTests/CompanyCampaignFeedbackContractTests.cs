using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyCampaignFeedbackContractTests
{
    [Fact]
    public async Task GetCampaignFeedback_ReturnsEnvelopeSchemaEligibilityAndRejectsInvalidQuery()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await CompanyCampaignReportingContractSeed.SeedAsync(factory, includeDeliveries: true, includeFeedback: true);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync($"/api/company/campaigns/{seed.CampaignId}/feedback?fromDateEgypt=2026-07-01&toDateEgypt=2026-07-13&outcome=Accepted&feedbackEligibility=Eligible");
        using var invalid = await client.GetAsync($"/api/company/campaigns/{seed.CampaignId}/feedback?outcome=Active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            var row = data.GetProperty("Items")[0];
            Assert.Equal("Accepted", row.GetProperty("Outcome").GetString());
            Assert.True(row.GetProperty("FeedbackQualifiesForScore").GetBoolean());
            Assert.True(row.TryGetProperty("PublicDoctorId", out _));
            Assert.False(row.TryGetProperty("DoctorName", out _));
            Assert.False(row.TryGetProperty("WalletId", out _));
            Assert.False(row.TryGetProperty("IdempotencyKey", out _));
        }

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
