using FluentValidation;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Services.DTOs.Payments;

namespace MediBridge.Services.Validators.Payments;

public sealed class MockTopUpRequestValidator : AbstractValidator<MockTopUpRequestDto>
{
    public MockTopUpRequestValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThanOrEqualTo(100m)
            .Must(MoneyRules.HasTwoOrFewerDecimalPlaces)
            .WithMessage("Amount must use no more than two decimal places.");
        RuleFor(request => request.Currency)
            .Equal("EGP")
            .WithMessage("Currency must be EGP.");
    }
}
