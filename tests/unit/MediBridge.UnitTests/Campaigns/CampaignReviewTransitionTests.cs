using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Validators.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CampaignReviewTransitionTests
{
    [Theory]
    [InlineData("Approved", null)]
    [InlineData("Rejected", "Rejected for review reasons.")]
    [InlineData("RevisionRequired", "Please revise the campaign.")]
    [InlineData("ChangesRequested", "Please revise the campaign.")]
    public void CampaignReview_PendingReview_AllowsSupportedDecisions(string decisionText, string? reason)
    {
        var decision = CampaignReviewTransitionPolicy.ParseDecision(decisionText);

        CampaignReviewTransitionPolicy.ValidateNewDecision(CampaignStatus.PendingReview, decision, reason);
    }

    [Theory]
    [InlineData(CampaignReviewDecision.Rejected)]
    [InlineData(CampaignReviewDecision.RevisionRequired)]
    public void CampaignReview_NonApprovalRequiresReason(CampaignReviewDecision decision)
    {
        Assert.Throws<WorkflowValidationException>(() =>
            CampaignReviewTransitionPolicy.ValidateNewDecision(CampaignStatus.PendingReview, decision, null));
    }

    [Fact]
    public void CampaignReview_DraftCannotBeApproved()
    {
        Assert.Throws<WorkflowConflictException>(() =>
            CampaignReviewTransitionPolicy.ValidateNewDecision(CampaignStatus.Draft, CampaignReviewDecision.Approved, null));
    }

    [Fact]
    public void CampaignReview_EquivalentReplayMatchesNormalizedPayload()
    {
        var equivalent = CampaignReviewTransitionPolicy.IsReplayEquivalent(
            CampaignReviewDecision.RevisionRequired,
            "  Please revise.  ",
            "  Internal note.  ",
            CampaignReviewDecision.RevisionRequired,
            "Please revise.",
            "Internal note.");

        Assert.True(equivalent);
    }

    [Fact]
    public void CampaignReview_ReplayWithConflictingDecisionIsNotEquivalent()
    {
        var equivalent = CampaignReviewTransitionPolicy.IsReplayEquivalent(
            CampaignReviewDecision.Approved,
            null,
            "Stored note.",
            CampaignReviewDecision.Rejected,
            "Conflicting replay.",
            "Stored note.");

        Assert.False(equivalent);
    }

    [Theory]
    [InlineData(CampaignReviewDecision.Approved, CampaignStatus.Approved)]
    [InlineData(CampaignReviewDecision.Rejected, CampaignStatus.Rejected)]
    [InlineData(CampaignReviewDecision.RevisionRequired, CampaignStatus.RevisionRequired)]
    public void CampaignReview_ResultStatusMatchesDecision(CampaignReviewDecision decision, CampaignStatus expectedStatus)
    {
        Assert.Equal(expectedStatus, CampaignReviewTransitionPolicy.GetResultStatus(decision));
    }

    [Fact]
    public void CampaignReview_RevisionRequiredIsCanonicalAndLegacyAliasParsesToIt()
    {
        var canonical = CampaignReviewTransitionPolicy.ParseDecision("RevisionRequired");
        var legacyAlias = CampaignReviewTransitionPolicy.ParseDecision("ChangesRequested");

        Assert.Equal("RevisionRequired", canonical.ToString());
        Assert.Equal(canonical, legacyAlias);
        Assert.Equal("RevisionRequired", CampaignReviewTransitionPolicy.GetResultStatus(canonical).ToString());
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("RevisionRequired")]
    [InlineData("ChangesRequested")]
    public void CampaignReview_ValidatorRequiresTrimmedPublicReasonForNonApproval(string decision)
    {
        var validator = new ReviewDecisionRequestDtoValidator();

        var result = validator.Validate(new ReviewDecisionRequestDto(decision, "   "));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CampaignReview_ReplayWithDifferentNotesIsNotEquivalent()
    {
        var equivalent = CampaignReviewTransitionPolicy.IsReplayEquivalent(
            CampaignReviewDecision.Approved,
            null,
            "Stored note.",
            CampaignReviewDecision.Approved,
            null,
            "Different note.");

        Assert.False(equivalent);
    }

    [Theory]
    [InlineData(CampaignReviewDecision.Approved, null, null, CampaignReviewDecision.Approved, null, null, true)]
    [InlineData(CampaignReviewDecision.RevisionRequired, "  Revise copy. ", " Internal. ", CampaignReviewDecision.RevisionRequired, "Revise copy.", "Internal.", true)]
    [InlineData(CampaignReviewDecision.Approved, null, null, CampaignReviewDecision.Rejected, null, null, false)]
    [InlineData(CampaignReviewDecision.Rejected, "Reason one", null, CampaignReviewDecision.Rejected, "Reason two", null, false)]
    [InlineData(CampaignReviewDecision.Approved, null, "Note one", CampaignReviewDecision.Approved, null, "Note two", false)]
    public void CampaignReview_ReplayEquivalenceIncludesDecisionReasonAndNotes(
        CampaignReviewDecision storedDecision,
        string? storedReason,
        string? storedNotes,
        CampaignReviewDecision requestedDecision,
        string? requestedReason,
        string? requestedNotes,
        bool expected)
    {
        var equivalent = CampaignReviewTransitionPolicy.IsReplayEquivalent(
            storedDecision,
            storedReason,
            storedNotes,
            requestedDecision,
            requestedReason,
            requestedNotes);

        Assert.Equal(expected, equivalent);
    }
}
