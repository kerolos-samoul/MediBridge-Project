using System.Net;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignReviewReadinessTests
{
    [Fact]
    public async Task AdminCampaignReviewReadiness_ReportsDistinctActionableIssues()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var submittedAtUtc = new DateTime(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);

        var pendingMedia = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(factory, actors.CompanyProfileId, submittedAtUtc);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, pendingMedia.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddPendingCampaignMediaAsync(factory, pendingMedia.CampaignId);

        var blankText = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(factory, actors.CompanyProfileId, submittedAtUtc.AddMinutes(1));
        await SetDescriptionAsync(factory, blankText.CampaignId, "   ");
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, blankText.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, blankText.CampaignId);

        var missingTarget = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(factory, actors.CompanyProfileId, submittedAtUtc.AddMinutes(2));
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, missingTarget.CampaignId);

        var missingMedia = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(factory, actors.CompanyProfileId, submittedAtUtc.AddMinutes(3));
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, missingMedia.CampaignId, actors.DoctorProfileId);

        using var client = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        var pendingIssues = await GetIssuesAsync(client, pendingMedia.CampaignId);
        var textIssues = await GetIssuesAsync(client, blankText.CampaignId);
        var targetIssues = await GetIssuesAsync(client, missingTarget.CampaignId);
        var mediaIssues = await GetIssuesAsync(client, missingMedia.CampaignId);

        Assert.Contains(pendingIssues, issue => issue.Contains("approved", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(pendingIssues, issue => issue.Contains("reviewable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(textIssues, issue => issue.Contains("description", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(targetIssues, issue => issue.Contains("target", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mediaIssues, issue => issue.Contains("reviewable", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SetDescriptionAsync(WebAppFactory factory, string campaignId, string description)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.FindAsync(campaignId);
        Assert.NotNull(campaign);
        campaign.Description = description;
        await context.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<string>> GetIssuesAsync(HttpClient client, string campaignId)
    {
        using var response = await client.GetAsync($"/api/admin/campaigns/{campaignId}/review-detail");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("Data")
            .GetProperty("reviewReadinessIssues")
            .EnumerateArray()
            .Select(issue => issue.GetString()!)
            .ToArray();
    }
}
