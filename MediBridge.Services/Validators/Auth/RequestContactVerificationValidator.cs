using FluentValidation;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

public sealed class RequestContactVerificationValidator : AbstractValidator<RequestContactVerificationDto>
{
    public RequestContactVerificationValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress();
        RuleFor(request => request.Channel).Equal(ContactVerificationChannel.Email);
    }
}
