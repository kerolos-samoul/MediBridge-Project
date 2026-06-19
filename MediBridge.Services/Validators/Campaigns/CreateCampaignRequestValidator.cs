using FluentValidation;
using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Validators.Campaigns;

public sealed class CreateCampaignRequestValidator : AbstractValidator<CreateCampaignRequestDto>
{
    public CreateCampaignRequestValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Description).NotEmpty().MaximumLength(4000);
        RuleFor(request => request.ClinicalResearchInfo).NotEmpty().MaximumLength(4000);
        RuleFor(request => request.AssetIds).NotNull().Must(ids => ids.Count > 0).WithMessage("At least one approved campaign asset is required.");
        RuleForEach(request => request.AssetIds).NotEmpty();
        RuleFor(request => request.TargetDoctorIds)
            .NotNull()
            .Must(ids => ids.Count is >= 1 and <= 100)
            .WithMessage("Campaign target doctor count must be between 1 and 100.");
        RuleForEach(request => request.TargetDoctorIds).NotEmpty();
        RuleFor(request => request.TargetDoctorIds)
            .Must(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Campaign target doctor ids must be unique.");
    }
}
