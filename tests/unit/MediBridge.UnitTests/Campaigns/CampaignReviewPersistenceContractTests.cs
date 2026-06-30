using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CampaignReviewPersistenceContractTests
{
    [Fact]
    public void CampaignLifecycle_ExposesRevisionRequiredWithoutChangingLegacyDecisionValue()
    {
        Assert.Contains("RevisionRequired", Enum.GetNames<CampaignStatus>());

        var decisionNames = Enum.GetNames<CampaignReviewDecision>();
        Assert.Contains("RevisionRequired", decisionNames);
        Assert.Equal(3, Convert.ToInt32(Enum.Parse<CampaignReviewDecision>("RevisionRequired")));
    }

    [Fact]
    public void CampaignPersistence_ExposesSubmissionAndReviewAuditFields()
    {
        Assert.NotNull(typeof(Campaign).GetProperty("SubmittedAtUtc"));
        Assert.NotNull(typeof(CampaignReviewHistory).GetProperty("PriorStatus"));
        Assert.NotNull(typeof(CampaignReviewHistory).GetProperty("ResultingStatus"));
        Assert.NotNull(typeof(CampaignReviewHistory).GetProperty("IdempotencyKey"));

        var attemptType = typeof(Campaign).Assembly.GetType(
            "MediBridge.Core.Entities.Campaigns.CampaignSubmissionAttempt");

        Assert.NotNull(attemptType);
        Assert.NotNull(attemptType!.GetProperty("CampaignId"));
        Assert.NotNull(attemptType.GetProperty("IdempotencyKey"));
        Assert.NotNull(attemptType.GetProperty("SubmittedAtUtc"));
        Assert.NotNull(attemptType.GetProperty("TargetCount"));
        Assert.NotNull(attemptType.GetProperty("EstimatedCost"));
        Assert.NotNull(attemptType.GetProperty("Currency"));
    }

    [Fact]
    public void CampaignRepository_ExposesModerationPersistenceOperations()
    {
        var repository = typeof(ICampaignRepository);

        Assert.NotNull(repository.GetMethod("FindActiveCampaignForUpdateAsync"));
        Assert.NotNull(repository.GetMethod("ReplaceCampaignTargetsAsync"));
        Assert.NotNull(repository.GetMethod("ListPendingReviewCampaignsAsync"));
        Assert.NotNull(repository.GetMethod("FindPendingReviewDetailAsync"));
        Assert.NotNull(repository.GetMethod("FindCampaignReviewByIdempotencyKeyAsync"));
        Assert.NotNull(repository.GetMethod("AddCampaignSubmissionAttemptAsync"));
        Assert.NotNull(repository.GetMethod("FindCampaignSubmissionAttemptByKeyAsync"));
        Assert.NotNull(repository.GetMethod("FindCurrentCampaignSubmissionAttemptAsync"));
        Assert.NotNull(repository.GetMethod("ListCampaignQueueRowsAsync"));

        Assert.NotNull(repository.Assembly.GetType(
            "MediBridge.Core.Interfaces.Campaigns.PendingCampaignReviewPageReadModel"));
    }
}
