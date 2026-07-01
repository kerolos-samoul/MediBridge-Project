using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Campaigns;

public sealed class TargetPreviewContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task TargetPreview_ReturnsEligibleCountAndEstimatedCostEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var admin = CreateAdminClient(factory, ids.AdminUserId);
        using var company = CreateCompanyClient(factory, ids.CompanyUserId);
        using var priceResponse = await admin.PutAsJsonAsync(
            WalletCampaignWorkflowRoutes.AdminDoctorPrice.Replace("{doctorId}", ids.DoctorProfileId, StringComparison.Ordinal),
            new { PricePerMessage = 50m });
        Assert.Equal(HttpStatusCode.OK, priceResponse.StatusCode);
        var campaignId = await CreateDraftAsync(company);

        using var response = await company.GetAsync(Route(campaignId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("eligibleDoctorCount").GetInt32());
        Assert.Equal(50m, data.GetProperty("estimatedTotalCost").GetDecimal());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task TargetPreview_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Doctor")]
    public async Task TargetPreview_WithNonCompanyToken_ReturnsForbiddenEnvelope(string role)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var company = CreateCompanyClient(factory, ids.CompanyUserId);
        var campaignId = await CreateDraftAsync(company);
        using var client = role == "Admin"
            ? CreateAdminClient(factory, ids.AdminUserId)
            : CreateDoctorClient(factory, ids.DoctorUserId);

        using var response = await client.GetAsync(Route(campaignId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task TargetPreview_ForUnknownCampaign_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await client.GetAsync(Route(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    private static async Task<string> CreateDraftAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            WalletCampaignWorkflowRoutes.CompanyCampaigns,
            new { Title = "Preview draft", Description = "Target preview contract" });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").GetProperty("campaignId").GetString()!;
    }

    private static string Route(string campaignId)
        => WalletCampaignWorkflowRoutes.CompanyCampaignTargetPreview.Replace("{campaignId}", campaignId, StringComparison.Ordinal);
}
