using FluentValidation;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Services.DTOs.Payments;

namespace MediBridge.Services.Validators.Wallets;

public sealed class MockTopUpRequestDtoValidator : AbstractValidator<MockTopUpRequestDto>
{
    public MockTopUpRequestDtoValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThan(0m)
            .Must(MoneyRules.HasTwoOrFewerDecimalPlaces)
            .WithMessage("Amount must use at most two decimal places.");
        RuleFor(request => request.Currency).Equal("EGP");
    }
}
