using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.ContractTests.Admin;

public sealed class AdminCampaignQueueContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task AdminCampaignQueue_ForApprovedCampaign_ReturnsDoctorLevelRowsEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(
            factory,
            actors.CompanyProfileId,
            actors.DoctorProfileId,
            assetStatus: StoredFileReviewStatus.Approved);
        using var client = CreateAdminClient(factory, actors.AdminUserId);
        using var reviewRequest = new HttpRequestMessage(HttpMethod.Post, ReviewRoute(campaign.CampaignId))
        {
            Content = JsonContent.Create(new { Decision = "Approved", Reason = (string?)null })
        };
        reviewRequest.Headers.Add("Idempotency-Key", "queue-contract-001");
        using var reviewResponse = await client.SendAsync(reviewRequest);
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        using var response = await client.GetAsync(QueueRoute(campaign.CampaignId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var row = Assert.Single(envelope.GetProperty("Data").EnumerateArray());
        Assert.Equal(actors.DoctorProfileId, row.GetProperty("doctorId").GetString());
        Assert.True(row.TryGetProperty("campaignSubmittedAtUtc", out var campaignSubmittedAtUtc));
        Assert.True(row.TryGetProperty("queuedAtUtc", out var queuedAtUtc));
        Assert.True(queuedAtUtc.GetDateTime() >= campaignSubmittedAtUtc.GetDateTime());
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("Doctor")]
    public async Task AdminCampaignQueue_WithNonAdminToken_ReturnsForbiddenEnvelope(string role)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = role == "Company"
            ? CreateCompanyClient(factory, actors.CompanyUserId)
            : CreateDoctorClient(factory, actors.DoctorUserId);

        using var response = await client.GetAsync(QueueRoute("missing"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    private static string ReviewRoute(string campaignId)
        => WalletCampaignWorkflowRoutes.AdminCampaignReview.Replace("{campaignId}", campaignId, StringComparison.Ordinal);

    private static string QueueRoute(string campaignId)
        => WalletCampaignWorkflowRoutes.AdminCampaignQueue.Replace("{campaignId}", campaignId, StringComparison.Ordinal);
}
