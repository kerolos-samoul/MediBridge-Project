using FluentValidation;
using MediBridge.Services.DTOs.Wallets;

namespace MediBridge.Services.Validators.Wallets;

public sealed class TopUpCompanyWalletRequestValidator : AbstractValidator<TopUpCompanyWalletRequestDto>
{
    public TopUpCompanyWalletRequestValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThanOrEqualTo(100m)
            .Must(HasNoMoreThanTwoDecimalPlaces)
            .WithMessage("Amount must use no more than two decimal places.");
        RuleFor(request => request.Description).MaximumLength(500);
    }

    private static bool HasNoMoreThanTwoDecimalPlaces(decimal amount)
    {
        return decimal.Round(amount, 2) == amount;
    }
}
