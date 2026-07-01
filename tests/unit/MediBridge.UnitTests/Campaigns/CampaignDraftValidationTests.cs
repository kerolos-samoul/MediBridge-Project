using FluentValidation.Validators;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CampaignDraftValidationTests
{
    [Theory]
    [InlineData("", "Description")]
    [InlineData("Title", "")]
    public void CampaignDraft_RequiresTitleAndDescription(string title, string description)
    {
        var validator = new CreateCampaignDraftRequestDtoValidator();

        var result = validator.Validate(new CreateCampaignDraftRequestDto(title, description, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CampaignAsset_MetadataIsValidWithoutMalwareScanningFields()
    {
        var validator = new CampaignAssetUploadRequestValidator();
        var request = new CampaignAssetUploadRequestDto("campaign.png", "image/png", 3);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(typeof(CampaignAssetUploadRequestDto).GetProperties(), property =>
            property.Name.Contains("Malware", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Scan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CampaignAsset_ContentTypeRuleMatchesPersistenceLimit()
    {
        var validator = new CampaignAssetUploadRequestValidator();
        var validators = validator
            .CreateDescriptor()
            .GetValidatorsForMember(nameof(CampaignAssetUploadRequestDto.ContentType))
            .Select(component => component.Validator)
            .OfType<IMaximumLengthValidator>()
            .ToArray();

        var maximumLengthValidator = Assert.Single(validators);
        Assert.Equal(120, maximumLengthValidator.Max);
    }

    [Fact]
    public void CampaignAsset_UsesCampaignMediaPurposeAndPendingReview()
    {
        Assert.Equal(StoredFilePurpose.CampaignMedia, (StoredFilePurpose)2);
        Assert.Equal(StoredFileReviewStatus.Pending, (StoredFileReviewStatus)1);
    }

    [Theory]
    [InlineData(CampaignStatus.Draft, true)]
    [InlineData(CampaignStatus.RevisionRequired, true)]
    [InlineData(CampaignStatus.PendingReview, false)]
    [InlineData(CampaignStatus.Approved, false)]
    [InlineData(CampaignStatus.Rejected, false)]
    public void CampaignContentEdit_IsAllowedOnlyForDraftOrRevisionRequired(CampaignStatus status, bool expected)
    {
        var actual = CampaignReviewTransitionPolicy.IsCompanyEditableStatus(status);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CampaignContentUpdate_ValidatorMatchesPersistenceLimits()
    {
        var validator = new UpdateCampaignRequestDtoValidator();
        var request = new UpdateCampaignRequestDto(
            new string('t', 201),
            new string('d', 4001),
            new string('c', 4001));

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Title");
        Assert.Contains(result.Errors, error => error.PropertyName == "Description");
        Assert.Contains(result.Errors, error => error.PropertyName == "ClinicalResearchInfo");
    }
}
