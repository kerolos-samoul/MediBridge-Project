using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase5CampaignWorkflowIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase5CampaignWorkflowIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SubmitCampaign_CreatesPendingReviewCampaignTargetsNoQueueAndAudit()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: 1000m);
        using var client = CreateCompanyClient(seed.Company.UserId);

        var response = await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.SingleAsync(campaign => campaign.CompanyId == seed.Company.CompanyId);
        Assert.Equal(CampaignStatus.PendingReview, campaign.Status);
        Assert.Equal(1, await context.CampaignTargets.CountAsync(target => target.CampaignId == campaign.Id));
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == campaign.Id));
        Assert.True(await context.AuditEvents.AnyAsync(audit => audit.EventType == "Phase5CampaignSubmissionSucceeded" && audit.TargetId == campaign.Id));
    }

    [Fact]
    public async Task SubmitCampaign_PreservesTargetSnapshotsAfterDoctorProfileChanges()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: 1000m);
        using var client = CreateCompanyClient(seed.Company.UserId);

        await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId]);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var doctor = await context.DoctorProfiles.SingleAsync(profile => profile.Id == seed.Doctor.DoctorId);
        doctor.Specialization = "Changed";
        doctor.ExperienceYears = 1;
        doctor.Location = "Changed";
        doctor.ActivityScore = 1m;
        doctor.PricePerMessage = 999m;
        await context.SaveChangesAsync();
        var target = await context.CampaignTargets.SingleAsync(target => target.DoctorId == seed.Doctor.DoctorId);

        Assert.Equal("Cardiology", target.SpecializationSnapshot);
        Assert.Equal(7, target.ExperienceYearsSnapshot);
        Assert.Equal("Cairo", target.LocationSnapshot);
        Assert.Equal(90m, target.ActivityScoreSnapshot);
        Assert.Equal(50m, target.PricePerMessageSnapshot);
    }

    [Fact]
    public async Task SubmitCampaign_RejectsInvalidTargetListsAllOrNothing()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: 10000m);
        var suspended = await Phase5CampaignQueueTestHelpers.SeedSuspendedDoctorAsync(factory.Services);
        var deleted = await Phase5CampaignQueueTestHelpers.SeedSoftDeletedDoctorAsync(factory.Services);
        var zeroPrice = await Phase5CampaignQueueTestHelpers.SeedZeroPriceDoctorAsync(factory.Services);
        using var client = CreateCompanyClient(seed.Company.UserId);

        var responses = new[]
        {
            await SubmitCampaignAsync(client, seed.AssetId, []),
            await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId, seed.Doctor.DoctorId]),
            await SubmitCampaignAsync(client, seed.AssetId, Enumerable.Range(0, 101).Select(index => $"doctor-{index}").ToArray()),
            await SubmitCampaignAsync(client, seed.AssetId, [suspended.DoctorId]),
            await SubmitCampaignAsync(client, seed.AssetId, [deleted.DoctorId]),
            await SubmitCampaignAsync(client, seed.AssetId, [zeroPrice.DoctorId])
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var companyCampaignIds = await context.Campaigns
            .Where(campaign => campaign.CompanyId == seed.Company.CompanyId)
            .Select(campaign => campaign.Id)
            .ToArrayAsync();
        Assert.Empty(companyCampaignIds);
        Assert.Equal(0, await context.CampaignTargets.CountAsync(target => companyCampaignIds.Contains(target.CampaignId)));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("asset")]
    [InlineData("target")]
    [InlineData("wallet")]
    public async Task SubmitCampaign_DenialAuditsUseActorRoleInsteadOfCompanyId(string scenario)
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: scenario == "wallet" ? 10m : 1000m);
        using var client = CreateCompanyClient(seed.Company.UserId);
        HttpResponseMessage response = scenario switch
        {
            "content" => await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId], title: ""),
            "asset" => await SubmitCampaignAsync(client, $"missing-{Guid.NewGuid():N}", [seed.Doctor.DoctorId]),
            "target" => await SubmitCampaignAsync(client, seed.AssetId, [$"missing-{Guid.NewGuid():N}"]),
            "wallet" => await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId]),
            _ => throw new InvalidOperationException("Unknown scenario.")
        };

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var audit = await context.AuditEvents
            .Where(audit => audit.ActorUserId == seed.Company.UserId && audit.Outcome == AuditOutcome.Denied)
            .OrderByDescending(audit => audit.CreatedAtUtc)
            .FirstAsync();

        Assert.Equal(UserRole.Company.ToString(), audit.ActorRole);
    }

    [Fact]
    public async Task SubmitCampaign_RejectsUnavailableOrCrossCompanyAssetsWithoutPersistence()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: 1000m);
        var otherCompany = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var otherAsset = await Phase5CampaignQueueTestHelpers.SeedApprovedCampaignAssetAsync(factory.Services, otherCompany.CompanyId);
        var pendingAsset = await SeedAssetAsync(seed.Company.CompanyId, StoredFileReviewStatus.Pending);
        using var client = CreateCompanyClient(seed.Company.UserId);

        var missingResponse = await SubmitCampaignAsync(client, $"missing-{Guid.NewGuid():N}", [seed.Doctor.DoctorId]);
        var pendingResponse = await SubmitCampaignAsync(client, pendingAsset, [seed.Doctor.DoctorId]);
        var crossCompanyResponse = await SubmitCampaignAsync(client, otherAsset, [seed.Doctor.DoctorId]);

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, pendingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, crossCompanyResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, await context.Campaigns.CountAsync(campaign => campaign.CompanyId == seed.Company.CompanyId));
    }

    [Fact]
    public async Task SubmitCampaign_IdempotencyReplaysSameRequestAndRejectsChangedContent()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: 1000m);
        using var client = CreateCompanyClient(seed.Company.UserId);
        var key = $"idem-{Guid.NewGuid():N}";

        var first = await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId], key);
        var replay = await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId], key);
        var conflict = await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId], key, title: "Changed");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaignIds = await context.Campaigns
            .Where(campaign => campaign.CompanyId == seed.Company.CompanyId)
            .Select(campaign => campaign.Id)
            .ToArrayAsync();
        Assert.Single(campaignIds);
        Assert.Equal(1, await context.CampaignTargets.CountAsync(target => campaignIds.Contains(target.CampaignId)));
    }

    [Fact]
    public async Task SubmitCampaign_ConcurrentSameKeySameContentCreatesSingleCampaignAndReplays()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: 1000m);
        var key = $"idem-{Guid.NewGuid():N}";
        using var firstClient = CreateCompanyClient(seed.Company.UserId);
        using var secondClient = CreateCompanyClient(seed.Company.UserId);

        var responses = await Task.WhenAll(
            SubmitCampaignAsync(firstClient, seed.AssetId, [seed.Doctor.DoctorId], key),
            SubmitCampaignAsync(secondClient, seed.AssetId, [seed.Doctor.DoctorId], key));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaignIds = await context.Campaigns
            .Where(campaign => campaign.CompanyId == seed.Company.CompanyId)
            .Select(campaign => campaign.Id)
            .ToArrayAsync();
        Assert.Single(campaignIds);
        Assert.Equal(1, await context.CampaignTargets.CountAsync(target => campaignIds.Contains(target.CampaignId)));
        Assert.Equal(1, await context.CampaignSubmissionRequests.CountAsync(request => request.CompanyId == seed.Company.CompanyId && request.IdempotencyKey == key));
    }

    [Fact]
    public async Task CompanyCampaigns_EnforceCompanyOwnershipForListDetailAndAssetUse()
    {
        await factory.InitializeDatabaseAsync();
        var owner = await SeedReadySubmissionAsync(availableBalance: 1000m);
        var other = await SeedReadySubmissionAsync(availableBalance: 1000m);
        using var ownerClient = CreateCompanyClient(owner.Company.UserId);
        using var otherClient = CreateCompanyClient(other.Company.UserId);
        var campaignId = await ReadCampaignIdAsync(await SubmitCampaignAsync(ownerClient, owner.AssetId, [owner.Doctor.DoctorId]));

        var otherList = await otherClient.GetAsync("/api/company/campaigns");
        var otherDetail = await otherClient.GetAsync($"/api/company/campaigns/{campaignId}");
        var crossAssetCreate = await SubmitCampaignAsync(otherClient, owner.AssetId, [other.Doctor.DoctorId]);

        using var document = await JsonDocument.ParseAsync(await otherList.Content.ReadAsStreamAsync());
        Assert.Equal(0, document.RootElement.GetProperty("Data").GetProperty("Items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, otherDetail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, crossAssetCreate.StatusCode);
    }

    [Fact]
    public async Task SubmitCampaign_RejectsInsufficientWalletWithoutCampaignQueueOrWalletEffects()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadySubmissionAsync(availableBalance: 10m);
        using var client = CreateCompanyClient(seed.Company.UserId);

        var response = await SubmitCampaignAsync(client, seed.AssetId, [seed.Doctor.DoctorId]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var companyCampaignIds = await context.Campaigns
            .Where(campaign => campaign.CompanyId == seed.Company.CompanyId)
            .Select(campaign => campaign.Id)
            .ToArrayAsync();
        Assert.Empty(companyCampaignIds);
        Assert.Equal(0, await context.CampaignTargets.CountAsync(target => companyCampaignIds.Contains(target.CampaignId)));
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(queue => companyCampaignIds.Contains(queue.CampaignId)));
        var wallet = await context.Wallets.SingleAsync(wallet => wallet.OwnerId == seed.Company.CompanyId);
        Assert.Equal(0, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == wallet.Id));
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == wallet.Id));
        Assert.Equal(10m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
    }

    private HttpClient CreateCompanyClient(string companyUserId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", companyUserId));
        return client;
    }

    private static async Task<string> ReadCampaignIdAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("Data").GetProperty("CampaignId").GetString()!;
    }

    private static Task<HttpResponseMessage> SubmitCampaignAsync(
        HttpClient client,
        string assetId,
        IReadOnlyList<string> doctorIds,
        string? idempotencyKey = null,
        string title = "Phase 5 campaign")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                Title = title,
                Description = "Phase 5 campaign description",
                ClinicalResearchInfo = "Phase 5 research context",
                AssetIds = new[] { assetId },
                TargetDoctorIds = doctorIds
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? $"idem-{Guid.NewGuid():N}");
        return client.SendAsync(request);
    }

    private async Task<CampaignWorkflowSeed> SeedReadySubmissionAsync(decimal availableBalance)
    {
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 50m, activityScore: 90m);
        var asset = await Phase5CampaignQueueTestHelpers.SeedApprovedCampaignAssetAsync(factory.Services, company.CompanyId);
        await Phase5CampaignQueueTestHelpers.SeedCompanyWalletAsync(factory.Services, company.CompanyId, company.UserId, availableBalance);
        return new CampaignWorkflowSeed(company, doctor, asset);
    }

    private async Task<string> SeedAssetAsync(string companyId, StoredFileReviewStatus reviewStatus)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = companyId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "phase5-asset.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{Guid.NewGuid():N}/asset.png",
            ReviewStatus = reviewStatus,
            UploadStatus = StoredFileUploadStatus.Stored
        };

        await context.StoredFiles.AddAsync(file);
        await context.SaveChangesAsync();
        return file.Id;
    }

    private sealed record CampaignWorkflowSeed(Phase5CompanySeed Company, Phase5DoctorSeed Doctor, string AssetId);
}
