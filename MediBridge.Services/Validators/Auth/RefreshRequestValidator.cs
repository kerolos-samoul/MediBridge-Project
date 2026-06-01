using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequestDto>
{
    public RefreshRequestValidator()
    {
        RuleFor(request => request.RefreshToken).NotEmpty();
    }
}
