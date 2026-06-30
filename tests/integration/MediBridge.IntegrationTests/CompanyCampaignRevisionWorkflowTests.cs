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

public sealed class CompanyCampaignRevisionWorkflowTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignRevisionWorkflowTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RevisionRequiredCampaign_CanBeUpdatedAndResubmittedWithoutWalletEffects()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedRevisionRequiredCampaignAsync(StoredFileReviewStatus.Pending);

        CampaignSubmissionDto first;
        CampaignSubmissionDto replay;
        using (var scope = factory.Services.CreateScope())
        {
            var workflow = scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>();
            var updated = await workflow.UpdateCampaignAsync(
                seed.CompanyUserId,
                seed.CampaignId,
                new UpdateCampaignRequestDto("Revised title", "Revised description", null));
            Assert.Equal(CampaignStatus.RevisionRequired, updated.Status);

            first = await workflow.SubmitCampaignAsync(
                seed.CompanyUserId,
                seed.CampaignId,
                "revision-submit-001");
            replay = await workflow.SubmitCampaignAsync(
                seed.CompanyUserId,
                seed.CampaignId,
                "revision-submit-001");
        }

        Assert.Equal(first, replay);
        Assert.Equal("PendingReview", first.Status);
        Assert.True(first.TargetCount >= 1);
        Assert.Equal(first.TargetCount * 50m, first.EstimatedCost);
        Assert.True(first.SubmittedAtUtc > seed.PreviousSubmittedAtUtc);

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.SingleAsync(item => item.Id == seed.CampaignId);
        Assert.Equal(CampaignStatus.PendingReview, campaign.Status);
        Assert.Equal("Revised title", campaign.Title);
        Assert.Null(campaign.ClinicalResearchInfo);
        Assert.Equal(1, await context.CampaignSubmissionAttempts.CountAsync(attempt => attempt.CampaignId == seed.CampaignId));
        Assert.Equal(first.TargetCount, await context.CampaignTargets.CountAsync(target => target.CampaignId == seed.CampaignId));
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(history => history.CampaignId == seed.CampaignId));
        Assert.Equal(1000m, await context.Wallets.Where(wallet => wallet.Id == seed.WalletId).Select(wallet => wallet.AvailableBalance).SingleAsync());
        Assert.Equal(0m, await context.Wallets.Where(wallet => wallet.Id == seed.WalletId).Select(wallet => wallet.ReservedBalance).SingleAsync());
        Assert.Equal(0, await context.WalletTransactions.CountAsync());
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task SubmissionAttempt_RejectsDifferentCurrentKeyAndHistoricalKeyReuse()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedRevisionRequiredCampaignAsync(StoredFileReviewStatus.Approved);

        using (var scope = factory.Services.CreateScope())
        {
            var workflow = scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>();
            await workflow.SubmitCampaignAsync(seed.CompanyUserId, seed.CampaignId, "revision-submit-old");
            await Assert.ThrowsAsync<Phase5ConflictException>(() => workflow.SubmitCampaignAsync(
                seed.CompanyUserId,
                seed.CampaignId,
                "revision-submit-other"));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var campaign = await context.Campaigns.SingleAsync(item => item.Id == seed.CampaignId);
            campaign.Status = CampaignStatus.RevisionRequired;
            campaign.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var workflow = scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>();
            await workflow.SubmitCampaignAsync(seed.CompanyUserId, seed.CampaignId, "revision-submit-new");
            await Assert.ThrowsAsync<Phase5ConflictException>(() => workflow.SubmitCampaignAsync(
                seed.CompanyUserId,
                seed.CampaignId,
                "revision-submit-old"));
        }

        using var verificationScope = factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(2, await verificationContext.CampaignSubmissionAttempts.CountAsync(attempt => attempt.CampaignId == seed.CampaignId));
    }

    [Fact]
    public async Task ReviewOutcome_ExposesPublicReasonButNotInternalNotes()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedRevisionRequiredCampaignAsync(StoredFileReviewStatus.Pending);

        CompanyReviewOutcomeDto outcome;
        using (var scope = factory.Services.CreateScope())
        {
            var workflow = scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>();
            outcome = await workflow.GetReviewOutcomeAsync(seed.CompanyUserId, seed.CampaignId);
        }

        Assert.Equal("RevisionRequired", outcome.Status);
        Assert.Equal("Please replace the media.", outcome.PublicReason);
        Assert.True(outcome.CanEdit);
        Assert.True(outcome.CanResubmit);
        Assert.Equal(0, outcome.QueuedCount);
    }

    [Fact]
    public async Task PendingReviewCampaign_CannotBeUpdated()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedRevisionRequiredCampaignAsync(StoredFileReviewStatus.Pending);
        using (var setupScope = factory.Services.CreateScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var campaign = await context.Campaigns.SingleAsync(item => item.Id == seed.CampaignId);
            campaign.Status = CampaignStatus.PendingReview;
            await context.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>();
        await Assert.ThrowsAsync<Phase5ConflictException>(() => workflow.UpdateCampaignAsync(
            seed.CompanyUserId,
            seed.CampaignId,
            new UpdateCampaignRequestDto("Changed", "Changed", null)));
    }

    private async Task<RevisionCampaignSeed> SeedRevisionRequiredCampaignAsync(StoredFileReviewStatus mediaStatus)
    {
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 50m);
        var walletId = await Phase5CampaignQueueTestHelpers.SeedCompanyWalletAsync(
            factory.Services,
            company.CompanyId,
            company.UserId,
            1000m);
        var previousSubmittedAtUtc = DateTime.UtcNow.AddHours(-1);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new MediBridgeIdentityUser
        {
            Id = $"revision-admin-{suffix}",
            UserName = $"revision-admin-{suffix}@medibridge.local",
            NormalizedUserName = $"REVISION-ADMIN-{suffix}@MEDIBRIDGE.LOCAL".ToUpperInvariant(),
            Email = $"revision-admin-{suffix}@medibridge.local",
            NormalizedEmail = $"REVISION-ADMIN-{suffix}@MEDIBRIDGE.LOCAL".ToUpperInvariant(),
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = previousSubmittedAtUtc
        };
        var campaign = new Campaign
        {
            Id = $"revision-campaign-{suffix}",
            CompanyId = company.CompanyId,
            Title = "Original title",
            Description = "Original description",
            ClinicalResearchInfo = null,
            Status = CampaignStatus.RevisionRequired,
            SubmittedAtUtc = previousSubmittedAtUtc,
            CreatedAtUtc = previousSubmittedAtUtc.AddHours(-1),
            UpdatedAtUtc = previousSubmittedAtUtc
        };
        var history = new CampaignReviewHistory
        {
            Id = $"revision-history-{suffix}",
            CampaignId = campaign.Id,
            AdminUserId = admin.Id,
            Decision = CampaignReviewDecision.RevisionRequired,
            IdempotencyKey = $"revision-review-{suffix}",
            Reason = "Please replace the media.",
            Notes = "Internal note must never be returned.",
            PriorStatus = CampaignStatus.PendingReview,
            ResultingStatus = CampaignStatus.RevisionRequired,
            CreatedAtUtc = previousSubmittedAtUtc
        };
        var media = new StoredFile
        {
            Id = $"revision-media-{suffix}",
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = company.CompanyId,
            RelatedCampaignId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "revision.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{suffix}/revision.png",
            StorageProvider = "TestStorage",
            StorageResourceType = StoredFileStorageResourceType.Image,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = mediaStatus,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = previousSubmittedAtUtc
        };

        await context.Users.AddAsync(admin);
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignReviewHistories.AddAsync(history);
        await context.StoredFiles.AddAsync(media);
        await context.SaveChangesAsync();
        return new RevisionCampaignSeed(company.UserId, campaign.Id, walletId, previousSubmittedAtUtc);
    }

    private sealed record RevisionCampaignSeed(
        string CompanyUserId,
        string CampaignId,
        string WalletId,
        DateTime PreviousSubmittedAtUtc);
}
