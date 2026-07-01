using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignReviewOutcomeTests
{
    [Fact]
    public async Task OwnedCampaignOutcome_ExposesPublicFieldsAndHidesInternalNotes()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        const string publicReason = "Correct the public dosage wording.";
        const string internalNotes = "Internal-only compliance escalation.";
        using var reviewRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaign.CampaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = "RevisionRequired", Reason = publicReason, Notes = internalNotes })
        };
        reviewRequest.Headers.Add("Idempotency-Key", "company-outcome-review");
        using var review = await admin.SendAsync(reviewRequest);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);

        using var response = await company.GetAsync($"/api/company/campaigns/{campaign.CampaignId}/review-outcome");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(internalNotes, body, StringComparison.Ordinal);
        Assert.DoesNotContain("notes", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(200, root.GetProperty("Code").GetInt32());
        Assert.Equal("Success", root.GetProperty("Message").GetString());
        var data = root.GetProperty("Data");
        Assert.Equal(7, data.EnumerateObject().Count());
        Assert.Equal(campaign.CampaignId, data.GetProperty("campaignId").GetString());
        Assert.Equal("RevisionRequired", data.GetProperty("status").GetString());
        Assert.Equal(publicReason, data.GetProperty("publicReason").GetString());
        Assert.NotEqual(default, data.GetProperty("decisionTimeUtc").GetDateTime());
        Assert.True(data.GetProperty("canEdit").GetBoolean());
        Assert.True(data.GetProperty("canResubmit").GetBoolean());
        Assert.Equal(0, data.GetProperty("queuedCount").GetInt32());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var history = await context.CampaignReviewHistories.AsNoTracking().SingleAsync(item => item.CampaignId == campaign.CampaignId);
        Assert.Equal(internalNotes, history.Notes);
    }
}
