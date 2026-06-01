using FluentValidation;
using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Validators.Auth;

internal sealed class VerificationMetadataDtoValidator : AbstractValidator<VerificationMetadataDto>
{
    public VerificationMetadataDtoValidator()
    {
        RuleFor(metadata => metadata.DocumentType).NotEmpty();
        RuleFor(metadata => metadata.OriginalFileName).NotEmpty();
        RuleFor(metadata => metadata.ContentType).NotEmpty();
        RuleFor(metadata => metadata.SizeBytes).GreaterThan(0);
        RuleFor(metadata => metadata.Reference).NotEmpty();
    }
}