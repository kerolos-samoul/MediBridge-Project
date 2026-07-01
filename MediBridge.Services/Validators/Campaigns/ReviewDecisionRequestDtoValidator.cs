using FluentValidation;
using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Validators.Campaigns;

public sealed class ReviewDecisionRequestDtoValidator : AbstractValidator<ReviewDecisionRequestDto>
{
    private static readonly string[] AllowedDecisions = ["Approved", "Rejected", "RevisionRequired", "ChangesRequested"];

    public ReviewDecisionRequestDtoValidator()
    {
        RuleFor(request => request.Decision)
            .NotEmpty()
            .Must(decision => AllowedDecisions.Contains(decision, StringComparer.Ordinal));
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .When(request => request.Decision is "Rejected" or "RevisionRequired" or "ChangesRequested");
        RuleFor(request => request.Reason).MaximumLength(1000);
        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}
