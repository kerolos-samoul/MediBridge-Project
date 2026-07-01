using System.Diagnostics;
using System.Net;
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

public sealed class AdminPendingCampaignListTests
{
    [Fact]
    public async Task AdminPendingCampaignList_UsesSameEligibilityForTotalAndItems()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var activeActors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var deletedActors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var submittedAtUtc = new DateTime(2026, 6, 26, 9, 0, 0, DateTimeKind.Utc);
        var visibleCampaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            activeActors.CompanyProfileId,
            submittedAtUtc);
        var hiddenCampaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            deletedActors.CompanyProfileId,
            submittedAtUtc.AddMinutes(1));

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var deletedCompany = await context.CompanyProfiles
                .IgnoreQueryFilters()
                .SingleAsync(company => company.Id == deletedActors.CompanyProfileId);
            deletedCompany.IsDeleted = true;
            deletedCompany.DeletedAtUtc = submittedAtUtc.AddMinutes(2);
            await context.SaveChangesAsync();
        }

        using var client = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, activeActors.AdminUserId);
        using var response = await client.GetAsync("/api/admin/campaigns/pending-review?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(visibleCampaign.CampaignId, item.GetProperty("campaignId").GetString());
        Assert.DoesNotContain(hiddenCampaign.CampaignId, json, StringComparison.Ordinal);
        Assert.DoesNotContain(deletedActors.CompanyProfileId, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminPendingCampaignList_ExcludesNonPendingAndPagesEqualTimeRowsDeterministically()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var submittedAtUtc = new DateTime(2026, 6, 26, 8, 0, 0, DateTimeKind.Utc);
        var expectedIds = new[]
        {
            "10000000000000000000000000000001",
            "10000000000000000000000000000002",
            "10000000000000000000000000000003"
        };
        await SeedCampaignsAsync(factory, actors.CompanyProfileId, submittedAtUtc, expectedIds);
        using var client = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var firstResponse = await client.GetAsync("/api/admin/campaigns/pending-review?PageNumber=1&PageSize=2");
        using var secondResponse = await client.GetAsync("/api/admin/campaigns/pending-review?PageNumber=2&PageSize=2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var firstIds = await ReadCampaignIdsAsync(firstResponse);
        var secondIds = await ReadCampaignIdsAsync(secondResponse);
        Assert.Equal(expectedIds[..2], firstIds);
        Assert.Equal(expectedIds[2..], secondIds);
        Assert.Equal(expectedIds, firstIds.Concat(secondIds));
    }

    [Fact]
    public async Task AdminPendingCampaignList_AfterWarmup_ReturnsTwentyItemsWithinOneSecond()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        await SeedPendingPageAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId, count: 20);
        using var client = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        const string route = "/api/admin/campaigns/pending-review?PageNumber=1&PageSize=20";

        using var warmup = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, warmup.StatusCode);

        var stopwatch = Stopwatch.StartNew();
        using var measured = await client.GetAsync(route);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, measured.StatusCode);
        var ids = await ReadCampaignIdsAsync(measured);
        Assert.Equal(20, ids.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Warmed request took {stopwatch.Elapsed}.");
    }

    private static async Task SeedCampaignsAsync(
        WebAppFactory factory,
        string companyId,
        DateTime submittedAtUtc,
        IReadOnlyList<string> pendingIds)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var excludedStatuses = new[]
        {
            CampaignStatus.Draft,
            CampaignStatus.Approved,
            CampaignStatus.Rejected,
            CampaignStatus.RevisionRequired,
            CampaignStatus.Active,
            CampaignStatus.Paused,
            CampaignStatus.Cancelled,
            CampaignStatus.Completed
        };
        foreach (var status in excludedStatuses)
        {
            await context.Campaigns.AddAsync(CreateCampaign(companyId, status, submittedAtUtc));
        }

        await context.Campaigns.AddAsync(CreateCampaign(companyId, CampaignStatus.PendingReview, submittedAtUtc, isDeleted: true));
        foreach (var id in pendingIds)
        {
            var campaign = CreateCampaign(companyId, CampaignStatus.PendingReview, submittedAtUtc);
            campaign.Id = id;
            await context.Campaigns.AddAsync(campaign);
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedPendingPageAsync(
        WebAppFactory factory,
        string companyId,
        string doctorId,
        int count)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var submittedAtUtc = new DateTime(2026, 6, 26, 10, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < count; index++)
        {
            var campaign = CreateCampaign(
                companyId,
                CampaignStatus.PendingReview,
                submittedAtUtc.AddMinutes(index));
            await context.Campaigns.AddAsync(campaign);
            await context.CampaignTargets.AddAsync(new CampaignTarget
            {
                CampaignId = campaign.Id,
                DoctorId = doctorId,
                SpecializationSnapshot = "Cardiology",
                ExperienceYearsSnapshot = 8,
                LocationSnapshot = "Cairo",
                ActivityScoreSnapshot = 90m,
                PricePerMessageSnapshot = 50m,
                CreatedAtUtc = campaign.SubmittedAtUtc!.Value
            });
            await context.StoredFiles.AddAsync(new StoredFile
            {
                OwnerType = StoredFileOwnerType.Campaign,
                OwnerId = campaign.Id,
                Purpose = StoredFilePurpose.CampaignMedia,
                OriginalFileName = $"performance-{index}.png",
                ContentType = "image/png",
                SizeBytes = 1024,
                StorageKey = $"integration/performance/{campaign.Id}.png",
                StorageState = StorageObjectState.Active,
                Visibility = StoredFileVisibility.Private,
                ReviewStatus = StoredFileReviewStatus.Approved,
                CreatedAtUtc = campaign.SubmittedAtUtc.Value
            });
        }

        await context.SaveChangesAsync();
    }

    private static Campaign CreateCampaign(
        string companyId,
        CampaignStatus status,
        DateTime submittedAtUtc,
        bool isDeleted = false)
        => new()
        {
            CompanyId = companyId,
            Title = $"{status} campaign",
            Description = "Campaign moderation list fixture.",
            Status = status,
            SubmittedAtUtc = submittedAtUtc,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? submittedAtUtc.AddMinutes(1) : null,
            CreatedAtUtc = submittedAtUtc.AddHours(-1),
            UpdatedAtUtc = submittedAtUtc
        };

    private static async Task<IReadOnlyList<string>> ReadCampaignIdsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("Data")
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("campaignId").GetString()!)
            .ToArray();
    }
}
