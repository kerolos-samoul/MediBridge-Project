using FluentValidation;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class ResendContactVerificationRequestValidator : AbstractValidator<ResendContactVerificationRequestDto>
{
    public ResendContactVerificationRequestValidator()
    {
        RuleFor(request => request.Contact)
            .NotEmpty()
            .EmailAddress();
        RuleFor(request => request.Channel)
            .Equal(ContactVerificationChannel.Email);
    }
}
