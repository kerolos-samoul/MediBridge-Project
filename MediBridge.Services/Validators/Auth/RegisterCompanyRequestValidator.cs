using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class RegisterCompanyRequestValidator : AbstractValidator<RegisterCompanyRequestDto>
{
    public RegisterCompanyRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress();
        RuleFor(request => request.Password).NotEmpty().MinimumLength(8);
        RuleFor(request => request.PhoneNumber).NotEmpty();
        RuleFor(request => request.CompanyName).NotEmpty();
        RuleFor(request => request.LicenseNumber).NotEmpty();
        RuleFor(request => request.ContactName).NotEmpty();
        RuleFor(request => request.VerificationMetadata).NotNull().SetValidator(new VerificationMetadataDtoValidator());
    }
}