using FluentValidation;
using MediBridge.Services.Config;
using MediBridge.Services.DTOs.Pricing;

namespace MediBridge.Services.Validators.Pricing;

public sealed class SetDoctorDeliverySettingsRequestDtoValidator : AbstractValidator<SetDoctorDeliverySettingsRequestDto>
{
    public SetDoctorDeliverySettingsRequestDtoValidator(DoctorDeliverySettingsOptions options)
    {
        RuleFor(request => request.DailyMessageLimit)
            .NotNull()
            .GreaterThanOrEqualTo(options.MinimumDailyMessageLimit)
            .LessThanOrEqualTo(options.MaximumDailyMessageLimit);

        RuleFor(request => request.MinimumWeeklyRequirement)
            .NotNull()
            .GreaterThanOrEqualTo(options.MinimumWeeklyRequirementFloor)
            .LessThanOrEqualTo(options.MaximumWeeklyRequirement);

        RuleFor(request => request.Reason)
            .NotEmpty()
            .MaximumLength(1000);
    }
}
