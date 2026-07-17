using FluentValidation;
using MediBridge.Services.DTOs.Pricing;

namespace MediBridge.Services.Validators.Pricing;

public sealed class DeactivateDoctorPricingRequestValidator : AbstractValidator<DeactivateDoctorPricingRequestDto>
{
    public DeactivateDoctorPricingRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .MaximumLength(1000);
    }
}
