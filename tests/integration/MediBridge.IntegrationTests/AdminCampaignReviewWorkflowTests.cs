using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminCampaignReviewWorkflowTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminCampaignReviewWorkflowTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ApproveCampaign_IsIdempotent_CreatesQueue_AndDoesNotMutateWalletState()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReviewableCampaignAsync(StoredFileReviewStatus.Approved);

        CampaignReviewResultDto first;
        CampaignReviewResultDto replay;
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminCampaignReviewService>();
            var request = new ReviewDecisionRequestDto("Approved", null, "Ready for delivery.");
            first = await service.ReviewCampaignAsync(seed.AdminUserId, seed.CampaignId, "approve-attempt-001", request);
            replay = await service.ReviewCampaignAsync(seed.AdminUserId, seed.CampaignId, "approve-attempt-001", request);
        }

        Assert.Equal("Approved", first.Decision);
        Assert.Equal("Approved", first.Status);
        Assert.Equal(1, first.QueuedCount);
        Assert.Equal(first, replay);

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.SingleAsync(candidate => candidate.Id == seed.CampaignId);
        var queue = await context.DoctorMessageQueues.SingleAsync(item => item.CampaignId == seed.CampaignId);

        Assert.Equal(CampaignStatus.Approved, campaign.Status);
        Assert.Equal(seed.SubmittedAtUtc, campaign.SubmittedAtUtc);
        Assert.Equal(seed.SubmittedAtUtc, queue.CampaignSubmittedAtUtc);
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(history => history.CampaignId == seed.CampaignId));
        Assert.Equal(0, await context.WalletTransactions.CountAsync());
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync());
        Assert.Equal(1000m, await context.Wallets.Where(wallet => wallet.Id == seed.WalletId).Select(wallet => wallet.AvailableBalance).SingleAsync());
    }

    [Fact]
    public async Task ApproveCampaign_RejectsPendingMedia_WithoutPersistingDecisionOrQueue()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReviewableCampaignAsync(StoredFileReviewStatus.Pending);

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminCampaignReviewService>();
            await Assert.ThrowsAsync<Phase5ValidationException>(() => service.ReviewCampaignAsync(
                seed.AdminUserId,
                seed.CampaignId,
                "approve-attempt-002",
                new ReviewDecisionRequestDto("Approved", null, null)));
        }

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(
            CampaignStatus.PendingReview,
            await context.Campaigns.Where(campaign => campaign.Id == seed.CampaignId).Select(campaign => campaign.Status).SingleAsync());
        Assert.Equal(0, await context.CampaignReviewHistories.CountAsync(history => history.CampaignId == seed.CampaignId));
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == seed.CampaignId));
    }

    [Fact]
    public async Task ReviewCampaign_WithSameIdempotencyKeyAndDifferentDecision_ReturnsConflictWithoutDuplicateSideEffects()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReviewableCampaignAsync(StoredFileReviewStatus.Approved);

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminCampaignReviewService>();
            await service.ReviewCampaignAsync(
                seed.AdminUserId,
                seed.CampaignId,
                "review-conflict-001",
                new ReviewDecisionRequestDto("Approved", null, "Ready for delivery."));
            await Assert.ThrowsAsync<Phase5ConflictException>(() => service.ReviewCampaignAsync(
                seed.AdminUserId,
                seed.CampaignId,
                "review-conflict-001",
                new ReviewDecisionRequestDto("Rejected", "Conflicting moderation decision.", null)));
        }

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(history => history.CampaignId == seed.CampaignId));
        Assert.Equal(1, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == seed.CampaignId));
        Assert.Equal(0, await context.WalletTransactions.CountAsync());
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync());
        Assert.Equal(1000m, await context.Wallets.Where(wallet => wallet.Id == seed.WalletId).Select(wallet => wallet.AvailableBalance).SingleAsync());
    }

    [Fact]
    public async Task ChangesRequestedAlias_PersistsAndReturnsCanonicalRevisionRequired()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReviewableCampaignAsync(StoredFileReviewStatus.Pending);

        CampaignReviewResultDto result;
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminCampaignReviewService>();
            result = await service.ReviewCampaignAsync(
                seed.AdminUserId,
                seed.CampaignId,
                "revision-attempt-001",
                new ReviewDecisionRequestDto("ChangesRequested", "Replace the pending media.", null));
        }

        Assert.Equal("RevisionRequired", result.Decision);
        Assert.Equal("RevisionRequired", result.Status);
        Assert.True(result.CanResubmit);

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var history = await context.CampaignReviewHistories.SingleAsync(item => item.CampaignId == seed.CampaignId);
        Assert.Equal(CampaignReviewDecision.RevisionRequired, history.Decision);
        Assert.Equal(CampaignStatus.RevisionRequired, history.ResultingStatus);
    }

    private async Task<ReviewableCampaignSeed> SeedReviewableCampaignAsync(StoredFileReviewStatus mediaStatus)
    {
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var walletId = await Phase5CampaignQueueTestHelpers.SeedCompanyWalletAsync(
            factory.Services,
            company.CompanyId,
            company.UserId,
            1000m);
        var submittedAtUtc = DateTime.UtcNow.AddMinutes(-5);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new MediBridgeIdentityUser
        {
            Id = $"admin-{suffix}",
            UserName = $"admin-{suffix}@medibridge.local",
            NormalizedUserName = $"ADMIN-{suffix}@MEDIBRIDGE.LOCAL".ToUpperInvariant(),
            Email = $"admin-{suffix}@medibridge.local",
            NormalizedEmail = $"ADMIN-{suffix}@MEDIBRIDGE.LOCAL".ToUpperInvariant(),
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var campaign = new Campaign
        {
            Id = $"campaign-{suffix}",
            CompanyId = company.CompanyId,
            Title = "007 moderation campaign",
            Description = "Ready for an administrator moderation decision.",
            ClinicalResearchInfo = null,
            Status = CampaignStatus.PendingReview,
            SubmittedAtUtc = submittedAtUtc,
            CreatedAtUtc = submittedAtUtc
        };
        var target = new CampaignTarget
        {
            Id = $"target-{suffix}",
            CampaignId = campaign.Id,
            DoctorId = doctor.DoctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 7,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 95m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = submittedAtUtc
        };
        var file = new StoredFile
        {
            Id = $"file-{suffix}",
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = company.CompanyId,
            RelatedCampaignId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "campaign.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{suffix}/campaign.png",
            StorageProvider = "TestStorage",
            StorageResourceType = StoredFileStorageResourceType.Image,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = mediaStatus,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            ReviewedAtUtc = mediaStatus == StoredFileReviewStatus.Approved ? DateTime.UtcNow : null,
            CreatedAtUtc = submittedAtUtc
        };

        await context.Users.AddAsync(admin);
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignTargets.AddAsync(target);
        await context.StoredFiles.AddAsync(file);
        await context.SaveChangesAsync();
        return new ReviewableCampaignSeed(admin.Id, campaign.Id, walletId, submittedAtUtc);
    }

    private sealed record ReviewableCampaignSeed(
        string AdminUserId,
        string CampaignId,
        string WalletId,
        DateTime SubmittedAtUtc);
}
