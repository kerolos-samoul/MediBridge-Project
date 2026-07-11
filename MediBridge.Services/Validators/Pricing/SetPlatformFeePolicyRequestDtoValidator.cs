using FluentValidation;
using MediBridge.Services.DTOs.Pricing;

namespace MediBridge.Services.Validators.Pricing;

public sealed class SetPlatformFeePolicyRequestDtoValidator : AbstractValidator<SetPlatformFeePolicyRequestDto>
{
    public SetPlatformFeePolicyRequestDtoValidator()
    {
        RuleFor(request => request.FeePercent)
            .NotNull()
            .GreaterThan(0m)
            .LessThanOrEqualTo(100m)
            .PrecisionScale(5, 2, false);

        RuleFor(request => request.Reason)
            .NotEmpty()
            .MaximumLength(1000);
    }
}
