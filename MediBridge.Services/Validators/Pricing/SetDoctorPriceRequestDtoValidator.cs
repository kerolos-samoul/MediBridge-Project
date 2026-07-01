using FluentValidation;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Services.DTOs.Pricing;

namespace MediBridge.Services.Validators.Pricing;

public sealed class SetDoctorPriceRequestDtoValidator : AbstractValidator<SetDoctorPriceRequestDto>
{
    public SetDoctorPriceRequestDtoValidator()
    {
        RuleFor(request => request.PricePerMessage)
            .NotNull()
            .GreaterThan(0m)
            .Must(amount => amount is not null && MoneyRules.HasTwoOrFewerDecimalPlaces(amount.Value))
            .WithMessage("Price per message must use at most two decimal places.");
        RuleFor(request => request.Reason).MaximumLength(500);
    }
}
