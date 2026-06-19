using FluentValidation;
using MediBridge.Services.DTOs.Doctors;

namespace MediBridge.Services.Validators.Doctors;

public sealed class EligibleDoctorSearchRequestValidator : AbstractValidator<EligibleDoctorSearchRequestDto>
{
    public EligibleDoctorSearchRequestValidator()
    {
        RuleFor(request => request.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.MinExperienceYears).GreaterThanOrEqualTo(0).When(request => request.MinExperienceYears.HasValue);
        RuleFor(request => request.MaxExperienceYears).GreaterThanOrEqualTo(0).When(request => request.MaxExperienceYears.HasValue);
        RuleFor(request => request.MinActivityScore).InclusiveBetween(0m, 100m).When(request => request.MinActivityScore.HasValue);
        RuleFor(request => request.MinPrice).GreaterThanOrEqualTo(0m).When(request => request.MinPrice.HasValue);
        RuleFor(request => request.MaxPrice).GreaterThanOrEqualTo(0m).When(request => request.MaxPrice.HasValue);
        RuleFor(request => request)
            .Must(request => !request.MinExperienceYears.HasValue || !request.MaxExperienceYears.HasValue || request.MinExperienceYears <= request.MaxExperienceYears)
            .WithMessage("Minimum experience must be less than or equal to maximum experience.");
        RuleFor(request => request)
            .Must(request => !request.MinPrice.HasValue || !request.MaxPrice.HasValue || request.MinPrice <= request.MaxPrice)
            .WithMessage("Minimum price must be less than or equal to maximum price.");
    }
}
