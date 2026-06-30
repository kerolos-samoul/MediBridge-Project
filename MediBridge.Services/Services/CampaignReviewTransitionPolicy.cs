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
            _ => throw new Phase5ValidationException("Validation failed.")
        };
    }

    internal static void ValidateNewDecision(
        CampaignStatus campaignStatus,
        CampaignReviewDecision decision,
        string? reason)
    {
        if (campaignStatus != CampaignStatus.PendingReview)
        {
            throw new Phase5ConflictException("Campaign is not in a reviewable state.");
        }

        if (decision is CampaignReviewDecision.Rejected or CampaignReviewDecision.RevisionRequired
            && string.IsNullOrWhiteSpace(reason))
        {
            throw new Phase5ValidationException("Validation failed.");
        }
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
            && string.Equals(Normalize(storedReason), Normalize(requestedReason), StringComparison.Ordinal)
            && string.Equals(Normalize(storedNotes), Normalize(requestedNotes), StringComparison.Ordinal);
    }

    internal static CampaignStatus GetResultStatus(CampaignReviewDecision decision)
    {
        return decision switch
        {
            CampaignReviewDecision.Approved => CampaignStatus.Approved,
            CampaignReviewDecision.Rejected => CampaignStatus.Rejected,
            CampaignReviewDecision.RevisionRequired => CampaignStatus.RevisionRequired,
            _ => throw new Phase5ConflictException("Campaign review decision is invalid.")
        };
    }

    internal static string GetCanonicalDecisionName(CampaignReviewDecision decision)
        => decision switch
        {
            CampaignReviewDecision.Approved => "Approved",
            CampaignReviewDecision.Rejected => "Rejected",
            CampaignReviewDecision.RevisionRequired => "RevisionRequired",
            _ => throw new Phase5ConflictException("Campaign review decision is invalid.")
        };

    internal static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static bool IsCompanyEditableStatus(CampaignStatus status)
        => status is CampaignStatus.Draft or CampaignStatus.RevisionRequired;
}
