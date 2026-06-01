using FluentValidation;
using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Validators.Auth;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class RegistrationValidationTests
{
    private readonly RegisterDoctorRequestValidator doctorValidator = new();
    private readonly RegisterCompanyRequestValidator companyValidator = new();

    [Fact]
    public void DoctorValidator_AcceptsValidRequest()
    {
        var result = doctorValidator.Validate(CreateDoctorRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DoctorValidator_RejectsMissingMetadataAndShortPassword()
    {
        var request = CreateDoctorRequest();
        request.Password = "short";
        request.VerificationMetadata.SizeBytes = 0;
        request.VerificationMetadata.Reference = string.Empty;

        var result = doctorValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterDoctorRequestDto.Password));
        Assert.Contains(result.Errors, error => error.PropertyName == "VerificationMetadata.SizeBytes");
        Assert.Contains(result.Errors, error => error.PropertyName == "VerificationMetadata.Reference");
    }

    [Fact]
    public void CompanyValidator_AcceptsValidRequest()
    {
        var result = companyValidator.Validate(CreateCompanyRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CompanyValidator_RejectsMissingLicenseAndMetadataSize()
    {
        var request = CreateCompanyRequest();
        request.LicenseNumber = string.Empty;
        request.VerificationMetadata.SizeBytes = 0;

        var result = companyValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterCompanyRequestDto.LicenseNumber));
        Assert.Contains(result.Errors, error => error.PropertyName == "VerificationMetadata.SizeBytes");
    }

    private static RegisterDoctorRequestDto CreateDoctorRequest()
    {
        return new RegisterDoctorRequestDto
        {
            Email = "doctor@example.com",
            Password = "Password1!",
            PhoneNumber = "5551234567",
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationMetadata = new VerificationMetadataDto
            {
                DocumentType = "License",
                OriginalFileName = "license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                Reference = "ref-1"
            }
        };
    }

    private static RegisterCompanyRequestDto CreateCompanyRequest()
    {
        return new RegisterCompanyRequestDto
        {
            Email = "company@example.com",
            Password = "Password1!",
            PhoneNumber = "5551234567",
            CompanyName = "Acme Pharma",
            LicenseNumber = "LIC-12345",
            ContactName = "Jane Doe",
            VerificationMetadata = new VerificationMetadataDto
            {
                DocumentType = "License",
                OriginalFileName = "company-license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 2048,
                Reference = "ref-2"
            }
        };
    }
}