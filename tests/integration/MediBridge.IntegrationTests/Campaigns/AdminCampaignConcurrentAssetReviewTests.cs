using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignConcurrentAssetReviewTests
{
    [Fact]
    public async Task ConcurrentConflictingAssetReviews_CommitExactlyOneDecision()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var assetId = await SeedPendingAssetAsync(factory, actors.CompanyProfileId);
        using var firstAdmin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var secondAdmin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        var approveTask = ReviewAsync(firstAdmin, assetId, "Approved", null);
        var rejectTask = ReviewAsync(secondAdmin, assetId, "Rejected", "Conflicting concurrent decision.");
        var responses = await Task.WhenAll(approveTask, rejectTask);
        using var approve = responses[0];
        using var reject = responses[1];

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var asset = await context.StoredFiles.AsNoTracking().SingleAsync(item => item.Id == assetId);
        Assert.True(asset.ReviewStatus is StoredFileReviewStatus.Approved or StoredFileReviewStatus.Rejected);
        Assert.Equal(1, await context.AuditEvents.CountAsync(item => item.EventType == "CampaignAssetReviewed" && item.TargetId == asset.OwnerId));
    }

    private static async Task<string> SeedPendingAssetAsync(WebAppFactory factory, string companyId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Concurrent asset review",
            Description = "Only one asset decision may commit."
        };
        var asset = new StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "concurrent.png",
            ContentType = "image/png",
            SizeBytes = 3,
            StorageKey = $"campaigns/{campaign.Id}/concurrent.png",
            ReviewStatus = StoredFileReviewStatus.Pending
        };
        await context.Campaigns.AddAsync(campaign);
        await context.StoredFiles.AddAsync(asset);
        await context.SaveChangesAsync();
        return asset.Id;
    }

    private static Task<HttpResponseMessage> ReviewAsync(HttpClient client, string assetId, string decision, string? reason)
        => client.PostAsJsonAsync($"/api/admin/campaign-assets/{assetId}/review", new { Decision = decision, Reason = reason });
}
