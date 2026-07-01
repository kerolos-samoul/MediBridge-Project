using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Admin;

public sealed class AdminCampaignAssetReviewContractTests : WalletCampaignContractTestBase
{
    [Theory]
    [InlineData("Approved", null)]
    [InlineData("Rejected", "Asset does not meet campaign standards.")]
    public async Task AdminCampaignAssetReview_ValidDecision_ReturnsEnvelope(string decision, string? reason)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(client, campaign.AssetId, decision, reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        Assert.Equal(decision, envelope.GetProperty("Data").GetProperty("reviewStatus").GetString());
    }

    [Fact]
    public async Task AdminCampaignAssetReview_RejectWithoutReason_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(client, campaign.AssetId, "Rejected", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task AdminCampaignAssetReview_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await ReviewAsync(client, "missing", "Approved", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("Doctor")]
    public async Task AdminCampaignAssetReview_WithNonAdminToken_ReturnsForbiddenEnvelope(string role)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = role == "Company"
            ? CreateCompanyClient(factory, actors.CompanyUserId)
            : CreateDoctorClient(factory, actors.DoctorUserId);

        using var response = await ReviewAsync(client, "missing", "Approved", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task AdminCampaignAssetReview_UnknownAsset_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(client, Guid.NewGuid().ToString("N"), "Approved", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    private static Task<HttpResponseMessage> ReviewAsync(HttpClient client, string assetId, string decision, string? reason)
        => client.PostAsJsonAsync(
            WalletCampaignWorkflowRoutes.AdminCampaignAssetReview.Replace("{assetId}", assetId, StringComparison.Ordinal),
            new { Decision = decision, Reason = reason });
}
