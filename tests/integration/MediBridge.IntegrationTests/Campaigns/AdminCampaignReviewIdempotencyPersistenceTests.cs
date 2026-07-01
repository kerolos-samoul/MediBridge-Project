using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignReviewIdempotencyPersistenceTests
{
    [Fact]
    public async Task ReviewIdempotencyKey_IsUniqueWithinCampaignAtDatabaseBoundary()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = CreateCampaign(actors.CompanyProfileId);
        await context.Campaigns.AddAsync(campaign);
        await context.SaveChangesAsync();
        await context.CampaignReviewHistories.AddRangeAsync(
            CreateReview(campaign.Id, actors.AdminUserId, "database-review-key"),
            CreateReview(campaign.Id, actors.AdminUserId, "database-review-key"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ReviewIdempotencyKey_CanBeReusedByDifferentCampaigns()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var firstCampaign = CreateCampaign(actors.CompanyProfileId);
        var secondCampaign = CreateCampaign(actors.CompanyProfileId);
        await context.Campaigns.AddRangeAsync(firstCampaign, secondCampaign);
        await context.SaveChangesAsync();
        await context.CampaignReviewHistories.AddRangeAsync(
            CreateReview(firstCampaign.Id, actors.AdminUserId, "shared-review-key"),
            CreateReview(secondCampaign.Id, actors.AdminUserId, "shared-review-key"));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.CampaignReviewHistories.CountAsync(item => item.IdempotencyKey == "shared-review-key"));
    }

    private static Campaign CreateCampaign(string companyId)
        => new()
        {
            CompanyId = companyId,
            Title = "Idempotency persistence",
            Description = "Review keys are scoped by campaign.",
            Status = CampaignStatus.PendingReview
        };

    private static CampaignReviewHistory CreateReview(string campaignId, string adminUserId, string idempotencyKey)
        => new()
        {
            CampaignId = campaignId,
            AdminUserId = adminUserId,
            Decision = CampaignReviewDecision.Approved,
            IdempotencyKey = idempotencyKey
        };
}
