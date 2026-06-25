using MediBridge.Core.Interfaces.Files;
using MediBridge.Repository.Storage;
using Xunit;

namespace MediBridge.UnitTests.Files;

public sealed class CloudinaryStorageMappingTests
{
    [Theory]
    [InlineData("image/png", "image")]
    [InlineData("image/jpeg", "image")]
    [InlineData("video/mp4", "video")]
    [InlineData("application/pdf", "raw")]
    [InlineData("text/plain", "raw")]
    public void ContentType_MapsToCloudinaryResourceType(string contentType, string expectedResourceType)
    {
        var resourceType = CloudinaryFileStorageProvider.MapResourceType(contentType);

        Assert.Equal(expectedResourceType, resourceType);
    }

    [Fact]
    public void UploadRequest_DoesNotAcceptCallerStorageKeyOrPublicId()
    {
        var requestProperties = typeof(FileStorageUpload).GetProperties();

        Assert.DoesNotContain(requestProperties, property =>
            property.Name.Contains("StorageKey", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("PublicId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FolderAndPublicId_AreGeneratedByProvider()
    {
        var folder = CloudinaryFileStorageProvider.BuildFolder("medibridge", "/campaigns/draft-1/");
        var publicId = CloudinaryFileStorageProvider.CreatePublicId("campaign.png");

        Assert.Equal("medibridge/campaigns/draft-1", folder);
        Assert.False(string.IsNullOrWhiteSpace(publicId));
        Assert.DoesNotContain("campaign.png", publicId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", publicId, StringComparison.Ordinal);
    }
}
