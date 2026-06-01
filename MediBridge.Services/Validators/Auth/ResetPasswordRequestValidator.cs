using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequestDto>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(request => request.ResetToken).NotEmpty();
        RuleFor(request => request.NewPassword).NotEmpty().MinimumLength(8);
    }
}
