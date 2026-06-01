using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

internal sealed class ResubmissionRequestValidator : AbstractValidator<ResubmissionRequestDto>
{
    public ResubmissionRequestValidator(IValidator<VerificationMetadataDto> verificationMetadataValidator)
    {
        RuleFor(request => request.ResubmissionToken).NotEmpty();
        RuleFor(request => request.VerificationMetadata)
            .NotNull()
            .SetValidator(verificationMetadataValidator);
        RuleFor(request => request.ExperienceYears)
            .GreaterThanOrEqualTo(0)
            .When(request => request.ExperienceYears.HasValue);
    }
}
