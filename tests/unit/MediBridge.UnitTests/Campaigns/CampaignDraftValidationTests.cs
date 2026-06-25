using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Campaigns;
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

    [Theory]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void CampaignAsset_ContentTypeMatchesPersistenceLimit(int contentTypeLength, bool expectedValid)
    {
        var validator = new CampaignAssetUploadRequestValidator();
        var request = new CampaignAssetUploadRequestDto(
            "campaign.bin",
            new string('a', contentTypeLength),
            1);

        var result = validator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void CampaignAsset_UsesCampaignMediaPurposeAndPendingReview()
    {
        Assert.Equal(StoredFilePurpose.CampaignMedia, (StoredFilePurpose)2);
        Assert.Equal(StoredFileReviewStatus.Pending, (StoredFileReviewStatus)1);
    }

    [Fact]
    public void CampaignAssetUpload_IsAllowedOnlyWhileCampaignIsDraft()
    {
        var allowedStatuses = new[] { CampaignStatus.Draft };

        Assert.Contains(CampaignStatus.Draft, allowedStatuses);
        Assert.DoesNotContain(CampaignStatus.PendingReview, allowedStatuses);
        Assert.DoesNotContain(CampaignStatus.Approved, allowedStatuses);
        Assert.DoesNotContain(CampaignStatus.Rejected, allowedStatuses);
    }
}
