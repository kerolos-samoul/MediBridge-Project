using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CampaignReviewMigrationTests
{
    [Fact]
    public void CampaignStatus_RevisionRequiredHasUniquePersistedValue()
    {
        var revisionRequired = CampaignStatus.RevisionRequired;

        Assert.NotEqual(CampaignStatus.Draft, revisionRequired);
        Assert.NotEqual(CampaignStatus.Rejected, revisionRequired);
        Assert.Equal(1, Enum.GetValues<CampaignStatus>().Count(status => status == revisionRequired));
    }

    [Fact]
    public async Task CampaignReviewFoundation_ModelContainsRequiredColumnsRelationshipsAndIndexes()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var model = context.Model;
        var campaignType = model.FindEntityType(typeof(Campaign));
        Assert.NotNull(campaignType);
        Assert.True(campaignType!.FindProperty("SubmittedAtUtc")?.IsNullable);
        Assert.Contains(campaignType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["Status", "SubmittedAtUtc", "Id"]));

        var attemptType = model.FindEntityType(typeof(CampaignSubmissionAttempt));
        Assert.NotNull(attemptType);
        Assert.Equal(18, attemptType!.FindProperty("EstimatedCost")?.GetPrecision());
        Assert.Equal(2, attemptType.FindProperty("EstimatedCost")?.GetScale());
        Assert.Equal(3, attemptType.FindProperty("Currency")?.GetMaxLength());
        Assert.Equal(128, attemptType.FindProperty("IdempotencyKey")?.GetMaxLength());
        Assert.Contains(attemptType.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Campaign)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual(["CampaignId"]));
        Assert.Contains(attemptType.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(["CampaignId", "IdempotencyKey"]));
        Assert.Contains(attemptType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CampaignId", "SubmittedAtUtc", "Id"]));

        var historyType = model.FindEntityType(typeof(CampaignReviewHistory));
        Assert.NotNull(historyType?.FindProperty("PriorStatus"));
        Assert.NotNull(historyType?.FindProperty("ResultingStatus"));
    }

    [Fact]
    public async Task CampaignReviewHistory_PriorAndResultingStatusRoundTrip()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = new Campaign
        {
            CompanyId = actors.CompanyProfileId,
            Title = "Review history state round trip",
            Description = "Persists prior and resulting campaign states.",
            Status = CampaignStatus.Rejected
        };
        var history = new CampaignReviewHistory
        {
            CampaignId = campaign.Id,
            AdminUserId = actors.AdminUserId,
            Decision = CampaignReviewDecision.RevisionRequired,
            Reason = "Revise the submitted content.",
            PriorStatus = CampaignStatus.PendingReview,
            ResultingStatus = CampaignStatus.RevisionRequired
        };
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignReviewHistories.AddAsync(history);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persisted = await context.CampaignReviewHistories.AsNoTracking().SingleAsync(item => item.Id == history.Id);

        Assert.Equal(CampaignStatus.PendingReview, persisted.PriorStatus);
        Assert.Equal(CampaignStatus.RevisionRequired, persisted.ResultingStatus);
    }

    [Fact]
    public async Task HardenCampaignReviewWorkflow_BackfillsLegacyReviewIdempotencyKeys()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260619132753_AddMockPaymentTransactions");
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaignId = Guid.NewGuid().ToString("N");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Campaigns]
                ([Id], [CompanyId], [Title], [MediaFileId], [VoiceNoteFileId], [ClinicalResearchInfo],
                 [Description], [Status], [CreatedAtUtc], [UpdatedAtUtc], [IsDeleted], [DeletedAtUtc])
            VALUES
                ({campaignId}, {actors.CompanyProfileId}, {"Migration backfill"}, {null}, {null}, {null},
                 {"Legacy review metadata is preserved."}, {(int)CampaignStatus.Rejected}, {DateTime.UtcNow}, {null}, {false}, {null})
            """);
        var reviewId = Guid.NewGuid().ToString("N");
        const string idempotencyKey = "legacy-review-key";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [CampaignReviewHistories]
                ([Id], [CampaignId], [AdminUserId], [Decision], [Reason], [Notes], [CreatedAtUtc], [CorrectsHistoryId])
            VALUES
                ({reviewId}, {campaignId}, {actors.AdminUserId}, {(int)CampaignReviewDecision.Rejected}, {"Legacy reason"}, {"IdempotencyKey=" + idempotencyKey}, {DateTime.UtcNow}, {null})
            """);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var migratedReview = await context.CampaignReviewHistories.AsNoTracking().SingleAsync(item => item.Id == reviewId);
        Assert.Equal(idempotencyKey, migratedReview.IdempotencyKey);
        Assert.Equal(CampaignStatus.PendingReview, migratedReview.PriorStatus);
        Assert.Equal(CampaignStatus.Rejected, migratedReview.ResultingStatus);
    }

    [Fact]
    public async Task CampaignReviewFoundation_CanonicalizesCurrentLegacyChangesRequest()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260619132753_AddMockPaymentTransactions");
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaignId = Guid.NewGuid().ToString("N");
        var reviewId = Guid.NewGuid().ToString("N");
        var reviewedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await InsertLegacyCampaignAsync(context, campaignId, actors.CompanyProfileId, CampaignStatus.Draft);
        await InsertLegacyReviewAsync(
            context,
            reviewId,
            campaignId,
            actors.AdminUserId,
            CampaignReviewDecision.ChangesRequested,
            "legacy-current-revision-key",
            reviewedAtUtc);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var campaign = await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaignId);
        var review = await context.CampaignReviewHistories.AsNoTracking().SingleAsync(item => item.Id == reviewId);
        Assert.Equal(CampaignStatus.RevisionRequired, campaign.Status);
        Assert.Equal(CampaignStatus.PendingReview, review.PriorStatus);
        Assert.Equal(CampaignStatus.RevisionRequired, review.ResultingStatus);
    }

    [Fact]
    public async Task CampaignReviewFoundation_PreservesHistoricalLegacyChangesRequestResult()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260619132753_AddMockPaymentTransactions");
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaignId = Guid.NewGuid().ToString("N");
        var revisionReviewId = Guid.NewGuid().ToString("N");
        var approvalReviewId = Guid.NewGuid().ToString("N");
        var firstReviewAtUtc = DateTime.UtcNow.AddMinutes(-10);
        await InsertLegacyCampaignAsync(context, campaignId, actors.CompanyProfileId, CampaignStatus.Approved);
        await InsertLegacyReviewAsync(
            context,
            revisionReviewId,
            campaignId,
            actors.AdminUserId,
            CampaignReviewDecision.ChangesRequested,
            "legacy-historical-revision-key",
            firstReviewAtUtc);
        await InsertLegacyReviewAsync(
            context,
            approvalReviewId,
            campaignId,
            actors.AdminUserId,
            CampaignReviewDecision.Approved,
            "legacy-historical-approval-key",
            firstReviewAtUtc.AddMinutes(5));

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var reviews = await context.CampaignReviewHistories
            .AsNoTracking()
            .Where(item => item.CampaignId == campaignId)
            .ToDictionaryAsync(item => item.Id);
        Assert.Equal(CampaignStatus.Draft, reviews[revisionReviewId].ResultingStatus);
        Assert.Equal(CampaignStatus.Approved, reviews[approvalReviewId].ResultingStatus);
    }

    private static Task InsertLegacyCampaignAsync(
        MediBridgeDbContext context,
        string campaignId,
        string companyId,
        CampaignStatus status)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Campaigns]
                ([Id], [CompanyId], [Title], [MediaFileId], [VoiceNoteFileId], [ClinicalResearchInfo],
                 [Description], [Status], [CreatedAtUtc], [UpdatedAtUtc], [IsDeleted], [DeletedAtUtc])
            VALUES
                ({campaignId}, {companyId}, {"Legacy campaign"}, {null}, {null}, {null},
                 {"Legacy moderation state."}, {(int)status}, {DateTime.UtcNow}, {null}, {false}, {null})
            """);
    }

    private static Task InsertLegacyReviewAsync(
        MediBridgeDbContext context,
        string reviewId,
        string campaignId,
        string adminUserId,
        CampaignReviewDecision decision,
        string idempotencyKey,
        DateTime createdAtUtc)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [CampaignReviewHistories]
                ([Id], [CampaignId], [AdminUserId], [Decision], [Reason], [Notes], [CreatedAtUtc], [CorrectsHistoryId])
            VALUES
                ({reviewId}, {campaignId}, {adminUserId}, {(int)decision}, {"Legacy reason"}, {"IdempotencyKey=" + idempotencyKey}, {createdAtUtc}, {null})
            """);
    }
}
