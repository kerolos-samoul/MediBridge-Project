using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

internal static class CampaignReviewTransitionPolicy
{
    internal static CampaignReviewDecision ParseDecision(string decision)
    {
        return decision switch
        {
            "Approved" => CampaignReviewDecision.Approved,
            "Rejected" => CampaignReviewDecision.Rejected,
            "RevisionRequired" => CampaignReviewDecision.RevisionRequired,
            "ChangesRequested" => CampaignReviewDecision.RevisionRequired,
            _ => throw new WorkflowValidationException("Validation failed.")
        };
    }

    internal static void ValidateNewDecision(
        CampaignStatus campaignStatus,
        CampaignReviewDecision decision,
        string? reason)
    {
        if (campaignStatus != CampaignStatus.PendingReview)
        {
            throw new WorkflowConflictException("Campaign is not in a reviewable state.");
        }

        if (decision is CampaignReviewDecision.Rejected or CampaignReviewDecision.RevisionRequired
            && string.IsNullOrWhiteSpace(reason))
        {
            throw new WorkflowValidationException("Validation failed.");
        }
    }

    internal static bool IsReplayEquivalent(
        CampaignReviewDecision storedDecision,
        string? storedReason,
        CampaignReviewDecision requestedDecision,
        string? requestedReason)
    {
        return storedDecision == requestedDecision
            && string.Equals(NormalizeReason(storedReason), NormalizeReason(requestedReason), StringComparison.Ordinal);
    }

    internal static bool IsReplayEquivalent(
        CampaignReviewDecision storedDecision,
        string? storedReason,
        string? storedNotes,
        CampaignReviewDecision requestedDecision,
        string? requestedReason,
        string? requestedNotes)
    {
        return storedDecision == requestedDecision
            && string.Equals(NormalizeReason(storedReason), NormalizeReason(requestedReason), StringComparison.Ordinal)
            && string.Equals(NormalizeReason(storedNotes), NormalizeReason(requestedNotes), StringComparison.Ordinal);
    }

    internal static CampaignStatus GetResultStatus(CampaignReviewDecision decision)
    {
        return decision switch
        {
            CampaignReviewDecision.Approved => CampaignStatus.Approved,
            CampaignReviewDecision.Rejected => CampaignStatus.Rejected,
            CampaignReviewDecision.RevisionRequired => CampaignStatus.RevisionRequired,
            _ => throw new WorkflowConflictException("Campaign review decision is invalid.")
        };
    }

    internal static string GetCanonicalDecisionName(CampaignReviewDecision decision)
    {
        return decision switch
        {
            CampaignReviewDecision.Approved => nameof(CampaignReviewDecision.Approved),
            CampaignReviewDecision.Rejected => nameof(CampaignReviewDecision.Rejected),
            CampaignReviewDecision.RevisionRequired => nameof(CampaignReviewDecision.RevisionRequired),
            _ => throw new WorkflowConflictException("Campaign review decision is invalid.")
        };
    }

    internal static string? NormalizeReason(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    internal static bool IsCompanyEditableStatus(CampaignStatus status)
        => status is CampaignStatus.Draft or CampaignStatus.RevisionRequired;
}
