using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Campaigns;

public sealed class CompanyCampaignDraftContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task CompanyCampaignDraft_WithValidRequest_ReturnsCreatedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await client.PostAsJsonAsync(
            WalletCampaignWorkflowRoutes.CompanyCampaigns,
            new { Title = "Cardiology update", Description = "A reviewable campaign draft.", ClinicalResearchInfo = "Study summary" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 201, "Created");
        var data = envelope.GetProperty("Data");
        Assert.Equal(ids.CompanyProfileId, data.GetProperty("companyId").GetString());
        Assert.Equal("Draft", data.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("campaignId").GetString()));
    }

    [Fact]
    public async Task CompanyCampaignDraft_WithInvalidRequest_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await client.PostAsJsonAsync(
            WalletCampaignWorkflowRoutes.CompanyCampaigns,
            new { Title = "", Description = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task CompanyCampaignDraft_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            WalletCampaignWorkflowRoutes.CompanyCampaigns,
            new { Title = "Draft", Description = "Description" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Fact]
    public async Task CompanyCampaignDraft_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateDoctorClient(factory, ids.DoctorUserId);

        using var response = await client.PostAsJsonAsync(
            WalletCampaignWorkflowRoutes.CompanyCampaigns,
            new { Title = "Draft", Description = "Description" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task CompanyCampaignUpdate_RevisionRequiredOwner_ReturnsUpdatedCampaignEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateRevisionRequiredCampaignFixtureAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-10));
        using var client = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await client.PutAsJsonAsync(
            Route(campaign.CampaignId),
            new
            {
                Title = "Corrected campaign",
                Description = "Corrected campaign description.",
                ClinicalResearchInfo = "Corrected clinical reference."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(campaign.CampaignId, data.GetProperty("campaignId").GetString());
        Assert.Equal("Corrected campaign", data.GetProperty("title").GetString());
        Assert.Equal("Corrected campaign description.", data.GetProperty("description").GetString());
        Assert.Equal("Corrected clinical reference.", data.GetProperty("clinicalResearchInfo").GetString());
        Assert.Equal("RevisionRequired", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CompanyCampaignUpdate_InvalidRequest_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateDraftCampaignFixtureAsync(factory, actors.CompanyProfileId);
        using var client = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await client.PutAsJsonAsync(
            Route(campaign.CampaignId),
            new { Title = " ", Description = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task CompanyCampaignUpdate_PendingReview_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreatePendingReviewCampaignFixtureAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        using var client = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await client.PutAsJsonAsync(
            Route(campaign.CampaignId),
            new { Title = "Blocked update", Description = "Blocked update description." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertEnvelopeAsync(response, 409);
    }

    [Fact]
    public async Task CompanyCampaignUpdate_NonOwner_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await CreateApprovedActorsAsync(factory);
        var nonOwner = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateDraftCampaignFixtureAsync(factory, owner.CompanyProfileId);
        using var client = CreateCompanyClient(factory, nonOwner.CompanyUserId);

        using var response = await client.PutAsJsonAsync(
            Route(campaign.CampaignId),
            new { Title = "Forbidden update", Description = "Forbidden update description." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task CompanyCampaignUpdate_UnknownCampaign_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await client.PutAsJsonAsync(
            Route(Guid.NewGuid().ToString("N")),
            new { Title = "Unknown", Description = "Unknown campaign." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    [Fact]
    public async Task CompanyCampaignUpdate_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            Route(Guid.NewGuid().ToString("N")),
            new { Title = "Unauthorized", Description = "Unauthorized campaign." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    private static string Route(string campaignId)
        => WalletCampaignWorkflowRoutes.CompanyCampaignUpdate.Replace("{campaignId}", campaignId, StringComparison.Ordinal);
}
