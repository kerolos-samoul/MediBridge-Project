using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequestDto>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Username).NotEmpty();
        RuleFor(request => request.Password).NotEmpty();
    }
}
