using FluentValidation;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Services.DTOs.Wallets;

namespace MediBridge.Services.Validators.Wallets;

public sealed class CreateWithdrawalRequestValidator : AbstractValidator<CreateWithdrawalRequestDto>
{
    public CreateWithdrawalRequestValidator()
    {
        RuleFor(request => request.Amount)
            .NotNull()
            .GreaterThan(0m)
            .Must(amount => amount is null || MoneyRules.HasTwoOrFewerDecimalPlaces(amount.Value))
            .WithMessage("Withdrawal amount must use no more than two decimal places.");
    }
}
