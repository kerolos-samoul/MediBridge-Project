using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Admin;

public sealed class AdminPendingCampaignReviewContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task AdminPendingCampaignReview_ReturnsBoundedPendingReviewEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var submittedAtUtc = new DateTime(2026, 6, 26, 8, 30, 0, DateTimeKind.Utc);
        var campaign = await CreatePendingReviewCampaignFixtureAsync(factory, actors.CompanyProfileId, submittedAtUtc);
        await AddTargetSnapshotFixtureAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await AddPendingCampaignMediaFixtureAsync(factory, campaign.CampaignId);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await client.GetAsync($"{WalletCampaignWorkflowRoutes.AdminPendingCampaignReviews}?PageNumber=1&PageSize=101");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("pageNumber").GetInt32());
        Assert.Equal(100, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());

        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(campaign.CampaignId, item.GetProperty("campaignId").GetString());
        Assert.Equal("PendingReview", item.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("descriptionSummary").GetString()));
        Assert.Equal(1, item.GetProperty("targetCount").GetInt32());
        Assert.False(item.GetProperty("hasApprovedMedia").GetBoolean());
        Assert.Contains(
            item.GetProperty("reviewReadinessIssues").EnumerateArray(),
            issue => issue.GetString()!.Contains("approved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminPendingCampaignReview_WithoutAuthentication_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(WalletCampaignWorkflowRoutes.AdminPendingCampaignReviews);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Fact]
    public async Task AdminPendingCampaignReview_ForNonAdmin_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await client.GetAsync(WalletCampaignWorkflowRoutes.AdminPendingCampaignReviews);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }
}
