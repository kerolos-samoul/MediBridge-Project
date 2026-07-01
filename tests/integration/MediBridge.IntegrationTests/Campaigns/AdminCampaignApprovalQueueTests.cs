using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignApprovalQueueTests
{
    [Fact]
    public async Task AdminCampaignApproval_CreatesQueueRowsOnceAndIsIdempotent()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var secondActors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var submittedAtUtc = new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Utc);
        var campaignId = await SeedSubmittedCampaignAsync(
            factory,
            actors.CompanyProfileId,
            [actors.DoctorProfileId, secondActors.DoctorProfileId],
            submittedAtUtc);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var first = await ReviewAsync(admin, campaignId, "approval-idempotency-001", "Approved", null);
        using var replay = await ReviewAsync(admin, campaignId, "approval-idempotency-001", "Approved", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var result = firstDocument.RootElement.GetProperty("Data");
        var decisionTimeUtc = result.GetProperty("decisionTimeUtc").GetDateTime();
        Assert.Equal(2, result.GetProperty("queuedCount").GetInt32());
        Assert.False(result.GetProperty("canResubmit").GetBoolean());
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var queueRows = await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(row => row.CampaignId == campaignId)
            .OrderBy(row => row.DoctorId)
            .ToListAsync();
        Assert.Equal(2, queueRows.Count);
        Assert.Equal(2, queueRows.Select(row => row.DoctorId).Distinct().Count());
        Assert.All(queueRows, row =>
        {
            Assert.Equal(QueueItemStatus.Queued, row.Status);
            Assert.Equal(submittedAtUtc, row.CampaignSubmittedAtUtc);
            Assert.Equal(decisionTimeUtc, row.QueuedAtUtc);
            Assert.Equal(decisionTimeUtc, row.CreatedAtUtc);
        });
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(row => row.CampaignId == campaignId));
        Assert.Equal(CampaignStatus.Approved, (await context.Campaigns.SingleAsync(row => row.Id == campaignId)).Status);
        Assert.Equal(
            StoredFileReviewStatus.Approved,
            await context.StoredFiles
                .Where(row => row.OwnerId == campaignId && row.Purpose == StoredFilePurpose.CampaignMedia)
                .Select(row => row.ReviewStatus)
                .SingleAsync());
    }

    private static async Task<string> SeedSubmittedCampaignAsync(
        WebAppFactory factory,
        string companyId,
        IReadOnlyList<string> doctorIds,
        DateTime submittedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Submitted campaign",
            Description = "Ready for approval.",
            Status = CampaignStatus.PendingReview,
            SubmittedAtUtc = submittedAtUtc,
            CreatedAtUtc = submittedAtUtc.AddMinutes(-5)
        };
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignTargets.AddRangeAsync(doctorIds.Select(doctorId => new CampaignTarget
        {
            CampaignId = campaign.Id,
            DoctorId = doctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 8,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 95m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = campaign.CreatedAtUtc.AddMinutes(1)
        }));
        await context.StoredFiles.AddAsync(new StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "approved-campaign.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"integration/{campaign.Id}/approved-campaign.png",
            StorageState = StorageObjectState.Active,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Approved,
            CreatedAtUtc = campaign.CreatedAtUtc
        });
        await context.SaveChangesAsync();
        return campaign.Id;
    }

    private static async Task<HttpResponseMessage> ReviewAsync(HttpClient client, string campaignId, string key, string decision, string? reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = decision, Reason = reason })
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
}
