using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class RegisterDoctorRequestValidator : AbstractValidator<RegisterDoctorRequestDto>
{
    public RegisterDoctorRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress();
        RuleFor(request => request.Password).NotEmpty().MinimumLength(8);
        RuleFor(request => request.PhoneNumber).NotEmpty();
        RuleFor(request => request.Specialization).NotEmpty();
        RuleFor(request => request.ExperienceYears).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Location).NotEmpty();
        RuleFor(request => request.VerificationMetadata).NotNull().SetValidator(new VerificationMetadataDtoValidator());
    }
}