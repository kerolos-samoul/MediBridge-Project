using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests.Campaigns;

public sealed class CompanyCampaignReviewOutcomeContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task CompanyCampaignReviewOutcome_Owner_ReturnsPublicEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var decisionTimeUtc = DateTime.UtcNow.AddMinutes(-2);
        var campaign = await CreateRevisionRequiredCampaignFixtureAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-10));
        await AddReviewHistoryAsync(factory, campaign.CampaignId, actors.AdminUserId, decisionTimeUtc);
        using var client = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await client.GetAsync(Route(campaign.CampaignId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(campaign.CampaignId, data.GetProperty("campaignId").GetString());
        Assert.Equal("RevisionRequired", data.GetProperty("status").GetString());
        Assert.Equal("Correct the public wording.", data.GetProperty("publicReason").GetString());
        Assert.Equal(decisionTimeUtc, data.GetProperty("decisionTimeUtc").GetDateTime());
        Assert.True(data.GetProperty("canEdit").GetBoolean());
        Assert.True(data.GetProperty("canResubmit").GetBoolean());
        Assert.Equal(0, data.GetProperty("queuedCount").GetInt32());
        Assert.False(data.TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task CompanyCampaignReviewOutcome_NonOwner_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await CreateApprovedActorsAsync(factory);
        var nonOwner = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateDraftCampaignFixtureAsync(factory, owner.CompanyProfileId);
        using var client = CreateCompanyClient(factory, nonOwner.CompanyUserId);

        using var response = await client.GetAsync(Route(campaign.CampaignId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task CompanyCampaignReviewOutcome_UnknownCampaign_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await client.GetAsync(Route(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    [Fact]
    public async Task CompanyCampaignReviewOutcome_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    private static async Task AddReviewHistoryAsync(
        ContractWebAppFactory factory,
        string campaignId,
        string adminUserId,
        DateTime decisionTimeUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.CampaignReviewHistories.AddAsync(new CampaignReviewHistory
        {
            CampaignId = campaignId,
            AdminUserId = adminUserId,
            Decision = CampaignReviewDecision.RevisionRequired,
            IdempotencyKey = "outcome-contract-review",
            Reason = "Correct the public wording.",
            Notes = "Internal note must stay hidden.",
            PriorStatus = CampaignStatus.PendingReview,
            ResultingStatus = CampaignStatus.RevisionRequired,
            CreatedAtUtc = decisionTimeUtc
        });
        await context.SaveChangesAsync();
    }

    private static string Route(string campaignId)
        => WalletCampaignWorkflowRoutes.CompanyCampaignReviewOutcome.Replace("{campaignId}", campaignId, StringComparison.Ordinal);
}
