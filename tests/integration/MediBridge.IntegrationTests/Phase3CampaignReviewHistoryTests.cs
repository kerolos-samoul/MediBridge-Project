using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3CampaignReviewHistoryTests
{
    [Fact]
    public async Task CampaignReviewCorrection_CreatesLinkedRecord_WithoutChangingOriginal()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var campaignId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId);
        var originalId = $"review-original-{Guid.NewGuid():N}";
        var correctionId = $"review-correction-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await unitOfWork.Campaigns.AddCampaignReviewHistoryAsync(originalId, campaignId, ids.AdminUserId, CampaignReviewDecision.Rejected);
        await unitOfWork.SaveChangesAsync();

        var originalDecision = await context.CampaignReviewHistories
            .Where(history => history.Id == originalId)
            .Select(history => history.Decision)
            .SingleAsync();

        await unitOfWork.Campaigns.AddCampaignReviewHistoryCorrectionAsync(
            correctionId,
            originalId,
            campaignId,
            ids.AdminUserId,
            CampaignReviewDecision.Approved,
            "Corrected admin review decision");
        await unitOfWork.SaveChangesAsync();

        var original = await context.CampaignReviewHistories.SingleAsync(history => history.Id == originalId);
        var correction = await context.CampaignReviewHistories.SingleAsync(history => history.Id == correctionId);

        Assert.Equal(originalDecision, original.Decision);
        Assert.Null(original.CorrectsHistoryId);
        Assert.Equal(originalId, correction.CorrectsHistoryId);
        Assert.Equal(CampaignReviewDecision.Approved, correction.Decision);
        Assert.Equal(2, await context.CampaignReviewHistories.CountAsync(history => history.CampaignId == campaignId));
    }
}
