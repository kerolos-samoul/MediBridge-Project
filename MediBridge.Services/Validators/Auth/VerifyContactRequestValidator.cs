using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class VerifyContactRequestValidator : AbstractValidator<VerifyContactRequestDto>
{
    public VerifyContactRequestValidator()
    {
        RuleFor(request => request.Channel).IsInEnum();
        RuleFor(request => request.VerificationToken).NotEmpty();
    }
}
