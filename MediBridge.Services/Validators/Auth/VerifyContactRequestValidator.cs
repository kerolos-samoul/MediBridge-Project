using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class VerifyContactRequestValidator : AbstractValidator<VerifyContactRequestDto>
{
    public VerifyContactRequestValidator()
    {
        RuleFor(request => request.Contact)
            .NotEmpty()
            .EmailAddress();
        RuleFor(request => request.Channel)
            .Equal(MediBridge.Core.Enums.ContactVerificationChannel.Email);
        RuleFor(request => request.VerificationToken)
            .NotEmpty()
            .Matches("^[0-9]{6,10}$");
    }
}
