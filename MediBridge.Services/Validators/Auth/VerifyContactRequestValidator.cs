using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class VerifyContactRequestValidator : AbstractValidator<VerifyContactRequestDto>
{
    public VerifyContactRequestValidator()
    {
        RuleFor(request => request.Channel).IsInEnum();
        RuleFor(request => request)
            .Must(request => !string.IsNullOrWhiteSpace(request.VerificationToken)
                             || (!string.IsNullOrWhiteSpace(request.Email) && !string.IsNullOrWhiteSpace(request.Otp)))
            .WithMessage("A verification token or email OTP is required.");

        When(request => string.IsNullOrWhiteSpace(request.VerificationToken), () =>
        {
            RuleFor(request => request.Email).NotEmpty().EmailAddress();
            RuleFor(request => request.Otp).Matches("^\\d{6}$");
        });
    }
}
