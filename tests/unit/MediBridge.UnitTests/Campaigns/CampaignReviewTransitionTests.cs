using System.Reflection;
using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CampaignReviewTransitionTests
{
    private static readonly Type? PolicyType = typeof(ICampaignWorkflowService).Assembly.GetType(
        "MediBridge.Services.Services.CampaignReviewTransitionPolicy");

    [Theory]
    [InlineData("RevisionRequired")]
    [InlineData("ChangesRequested")]
    public void ParseDecision_CanonicalizesRevisionRequired(string input)
    {
        Assert.NotNull(PolicyType);
        var result = Invoke("ParseDecision", input);

        Assert.Equal(CampaignReviewDecision.RevisionRequired, result);
        Assert.Equal("RevisionRequired", Invoke("GetCanonicalDecisionName", result!));
    }

    [Fact]
    public void ValidateNewDecision_RejectsMissingReasonAndStaleStatus()
    {
        Assert.IsType<Phase5ValidationException>(Assert.Throws<TargetInvocationException>(() =>
            Invoke("ValidateNewDecision", CampaignStatus.PendingReview, CampaignReviewDecision.Rejected, null)).InnerException);
        Assert.IsType<Phase5ConflictException>(Assert.Throws<TargetInvocationException>(() =>
            Invoke("ValidateNewDecision", CampaignStatus.Approved, CampaignReviewDecision.Approved, null)).InnerException);
    }

    [Fact]
    public void ReplayEquivalence_IncludesNormalizedReasonAndNotes()
    {
        Assert.Equal(true, Invoke(
            "IsReplayEquivalent",
            CampaignReviewDecision.Rejected,
            " reason ",
            " note ",
            CampaignReviewDecision.Rejected,
            "reason",
            "note"));
        Assert.Equal(false, Invoke(
            "IsReplayEquivalent",
            CampaignReviewDecision.Rejected,
            "reason",
            "first",
            CampaignReviewDecision.Rejected,
            "reason",
            "second"));
    }

    private static object? Invoke(string methodName, params object?[] arguments)
    {
        Assert.NotNull(PolicyType);
        var method = PolicyType!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(method => method.Name == methodName && method.GetParameters().Length == arguments.Length);
        return method.Invoke(null, arguments);
    }
}
