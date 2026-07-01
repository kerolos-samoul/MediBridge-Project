using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignApprovalPrerequisiteTests
{
    [Fact]
    public async Task PendingMedia_IsReviewableButApprovalRequiresSeparatelyApprovedMedia_AndDoesNotChangeAssetStatuses()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        var pendingAssetId = await WalletCampaignWorkflowTestHelpers.AddPendingCampaignMediaAsync(factory, campaign.CampaignId);
        var rejectedAssetId = await WalletCampaignWorkflowTestHelpers.AddRejectedCampaignMediaAsync(factory, campaign.CampaignId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var detailResponse = await admin.GetAsync($"/api/admin/campaigns/{campaign.CampaignId}/review-detail");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using (var detailDocument = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync()))
        {
            var media = Assert.Single(detailDocument.RootElement.GetProperty("Data").GetProperty("reviewableMediaAssets").EnumerateArray());
            Assert.Equal("Pending", media.GetProperty("reviewStatus").GetString());
        }

        using var blocked = await ReviewAsync(admin, campaign.CampaignId, "pending-media-blocked", "Approved");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("approved campaign media", await ReadMessageAsync(blocked), StringComparison.OrdinalIgnoreCase);

        var approvedAssetId = await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        using var approved = await ReviewAsync(admin, campaign.CampaignId, "pending-media-approved", "Approved");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var statuses = await context.StoredFiles
            .AsNoTracking()
            .Where(file => file.Id == pendingAssetId || file.Id == rejectedAssetId || file.Id == approvedAssetId)
            .ToDictionaryAsync(file => file.Id, file => file.ReviewStatus);
        Assert.Equal(StoredFileReviewStatus.Pending, statuses[pendingAssetId]);
        Assert.Equal(StoredFileReviewStatus.Rejected, statuses[rejectedAssetId]);
        Assert.Equal(StoredFileReviewStatus.Approved, statuses[approvedAssetId]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DeletedOrSupersededApprovedMedia_DoesNotSatisfyApproval(bool deleted, bool superseded)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await AddInactiveApprovedMediaAsync(factory, campaign.CampaignId, deleted, superseded);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var response = await ReviewAsync(admin, campaign.CampaignId, $"inactive-approved-{deleted}-{superseded}", "Approved");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("approved campaign media", await ReadMessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false, "campaign text")]
    [InlineData(false, true, "submitted target")]
    public async Task ApprovalFailsBeforeMutation_WhenCampaignTextOrTargetsAreMissing(
        bool blankText,
        bool omitTargets,
        string expectedMessageFragment)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        if (!omitTargets)
        {
            await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        }

        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        if (blankText)
        {
            using var setupScope = factory.Services.CreateScope();
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var storedCampaign = await setupContext.Campaigns.SingleAsync(item => item.Id == campaign.CampaignId);
            storedCampaign.Title = " ";
            storedCampaign.Description = " ";
            await setupContext.SaveChangesAsync();
        }

        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var response = await ReviewAsync(admin, campaign.CampaignId, $"missing-prerequisite-{blankText}-{omitTargets}", "Approved");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedMessageFragment, await ReadMessageAsync(response), StringComparison.OrdinalIgnoreCase);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(CampaignStatus.PendingReview, (await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaign.CampaignId)).Status);
        Assert.Equal(0, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaign.CampaignId));
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaign.CampaignId));
    }

    [Fact]
    public async Task ApprovalFailsBeforeMutation_WhenOwningCompanyIsNotApproved()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        using (var setupScope = factory.Services.CreateScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var companyUser = await setupContext.Users.SingleAsync(user => user.Id == actors.CompanyUserId);
            companyUser.AccountStatus = AccountStatus.Suspended;
            await setupContext.SaveChangesAsync();
        }

        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var response = await ReviewAsync(admin, campaign.CampaignId, "unapproved-company", "Approved");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("approved and active", await ReadMessageAsync(response), StringComparison.OrdinalIgnoreCase);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaign.CampaignId));
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaign.CampaignId));
    }

    private static async Task AddInactiveApprovedMediaAsync(
        WebAppFactory factory,
        string campaignId,
        bool deleted,
        bool superseded)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.StoredFiles.AddAsync(new StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaignId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "inactive-approved.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"integration/{campaignId}/{Guid.NewGuid():N}.png",
            StorageState = deleted ? StorageObjectState.Deleted : StorageObjectState.Active,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null,
            SupersededByFileId = superseded ? Guid.NewGuid().ToString("N") : null,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> ReviewAsync(
        HttpClient client,
        string campaignId,
        string idempotencyKey,
        string decision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = decision, Reason = (string?)null })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Message").GetString() ?? string.Empty;
    }
}
