using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests.Admin;

public sealed class AdminCampaignReviewContractTests : WalletCampaignContractTestBase
{
    [Theory]
    [InlineData("Approved", null, "Approved", "Approved", false)]
    [InlineData("Rejected", "Campaign rejected.", "Rejected", "Rejected", false)]
    [InlineData("RevisionRequired", "Please revise the campaign.", "RevisionRequired", "RevisionRequired", true)]
    public async Task AdminCampaignReview_ValidDecision_ReturnsEnvelope(
        string decision,
        string? reason,
        string expectedDecision,
        string expectedStatus,
        bool expectedCanResubmit)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(
            factory,
            actors.CompanyProfileId,
            actors.DoctorProfileId,
            assetStatus: decision == "Approved"
                ? StoredFileReviewStatus.Approved
                : StoredFileReviewStatus.Pending);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        var decisionAfterUtc = DateTime.UtcNow.AddSeconds(-1);
        using var response = await ReviewAsync(client, campaign.CampaignId, "review-contract-001", decision, reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(expectedDecision, data.GetProperty("decision").GetString());
        Assert.Equal(expectedStatus, data.GetProperty("status").GetString());
        Assert.Equal(expectedCanResubmit, data.GetProperty("canResubmit").GetBoolean());
        var decisionTimeUtc = data.GetProperty("decisionTimeUtc").GetDateTime();
        Assert.InRange(decisionTimeUtc, decisionAfterUtc, DateTime.UtcNow.AddSeconds(1));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var history = await context.CampaignReviewHistories.AsNoTracking().SingleAsync(item => item.CampaignId == campaign.CampaignId);
        Assert.Equal(CampaignStatus.PendingReview, history.PriorStatus);
        Assert.Equal(Enum.Parse<CampaignStatus>(expectedStatus), history.ResultingStatus);
        Assert.Equal(decisionTimeUtc, history.CreatedAtUtc);

        using var replay = await ReviewAsync(client, campaign.CampaignId, "review-contract-001", decision, reason);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayEnvelope = await AssertEnvelopeAsync(replay, 200, "Success");
        Assert.Equal(
            decisionTimeUtc,
            replayEnvelope.GetProperty("Data").GetProperty("decisionTimeUtc").GetDateTime());
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaign.CampaignId));
    }

    [Fact]
    public async Task AdminCampaignReview_MissingIdempotencyHeader_ReturnsValidationEnvelope()
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

        using var response = await client.PostAsJsonAsync(Route(campaign.CampaignId), new { Decision = "Approved", Reason = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task AdminCampaignReview_UnsupportedDecision_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(client, campaign.CampaignId, "review-contract-invalid", "Pending", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task AdminCampaignReview_InvalidTransition_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId, CampaignStatus.Draft);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(client, campaign.CampaignId, "review-contract-002", "Approved", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertEnvelopeAsync(response, 409);
    }

    [Fact]
    public async Task AdminCampaignReview_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await ReviewAsync(client, "missing", "review-contract-003", "Approved", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("Doctor")]
    public async Task AdminCampaignReview_WithNonAdminToken_ReturnsForbiddenEnvelope(string role)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = role == "Company"
            ? CreateCompanyClient(factory, actors.CompanyUserId)
            : CreateDoctorClient(factory, actors.DoctorUserId);

        using var response = await ReviewAsync(client, "missing", "review-contract-004", "Approved", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task AdminCampaignReview_UnknownCampaign_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(client, Guid.NewGuid().ToString("N"), "review-contract-005", "Approved", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    [Fact]
    public async Task AdminCampaignReview_ConflictingReplay_ReturnsConflictEnvelope()
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

        using var first = await ReviewAsync(client, campaign.CampaignId, "review-contract-006", "Approved", null);
        using var conflict = await ReviewAsync(client, campaign.CampaignId, "review-contract-006", "Rejected", "Conflicting replay.");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertEnvelopeAsync(conflict, 409);
    }

    [Fact]
    public async Task AdminCampaignReview_SameDecisionWithDifferentReason_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId);
        using var client = CreateAdminClient(factory, actors.AdminUserId);
        const string key = "review-contract-reason-conflict";

        using var first = await ReviewAsync(client, campaign.CampaignId, key, "Rejected", "First rejection reason.");
        using var conflict = await ReviewAsync(client, campaign.CampaignId, key, "Rejected", "Different rejection reason.");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertEnvelopeAsync(conflict, 409);
    }

    [Fact]
    public async Task AdminCampaignReview_SameDecisionWithDifferentNotes_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId);
        using var client = CreateAdminClient(factory, actors.AdminUserId);
        const string key = "review-contract-notes-conflict";

        using var first = await ReviewAsync(
            client,
            campaign.CampaignId,
            key,
            "ChangesRequested",
            "Please revise.",
            "Internal note one.");
        using var conflict = await ReviewAsync(
            client,
            campaign.CampaignId,
            key,
            "ChangesRequested",
            "Please revise.",
            "Internal note two.");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertEnvelopeAsync(conflict, 409);
    }

    private static async Task<HttpResponseMessage> ReviewAsync(
        HttpClient client,
        string campaignId,
        string key,
        string decision,
        string? reason,
        string? notes = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route(campaignId))
        {
            Content = JsonContent.Create(new { Decision = decision, Reason = reason, Notes = notes })
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static string Route(string campaignId)
        => WalletCampaignWorkflowRoutes.AdminCampaignReview.Replace("{campaignId}", campaignId, StringComparison.Ordinal);
}
