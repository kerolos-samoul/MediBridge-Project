using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Files;

namespace MediBridge.Services.Validators.Files;

public sealed class FileReviewRequestValidator
{
    public IReadOnlyList<string> Validate(FileReviewRequestDto request)
    {
        var errors = new List<string>();
        if (RequiresReason(request.Decision) && string.IsNullOrWhiteSpace(request.Reason))
        {
            errors.Add("Reason is required for this review decision.");
        }

        if (request.Decision == FileReviewDecision.Correction && string.IsNullOrWhiteSpace(request.CorrectsReviewId))
        {
            errors.Add("CorrectsReviewId is required for correction decisions.");
        }

        return errors;
    }

    private static bool RequiresReason(FileReviewDecision decision)
    {
        return decision is FileReviewDecision.Rejected
            or FileReviewDecision.Quarantined
            or FileReviewDecision.ReplacementRequested
            or FileReviewDecision.Correction;
    }
}
