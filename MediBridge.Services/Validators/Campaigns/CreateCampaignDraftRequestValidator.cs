using FluentValidation;
using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Validators.Campaigns;

public sealed class CreateCampaignDraftRequestValidator : AbstractValidator<CreateCampaignDraftRequestDto>
{
    public CreateCampaignDraftRequestValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Description).NotEmpty().MaximumLength(4000);
        RuleFor(request => request.ClinicalResearchInfo).MaximumLength(4000);
    }
}
