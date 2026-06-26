using CloudinaryDotNet;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Repository.Storage;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task SignedReadUrl_WithNormalApiSecret_UsesPrivateDownloadUrlWithoutAuthToken()
    {
        var provider = new CloudinaryFileStorageProvider(
            new Cloudinary("cloudinary://123456789012345:not-a-hex-api-secret@demo-cloud"),
            Options.Create(new CloudinaryStorageOptions
            {
                CloudinaryUrl = "cloudinary://123456789012345:not-a-hex-api-secret@demo-cloud",
                FolderPrefix = "medibridge",
                UseSecureUrls = true,
                SignedUrlMinutes = 5
            }));

        var signedUrl = await provider.CreateSignedReadUrlAsync(
            "medibridge/campaigns/file-id",
            "raw",
            TimeSpan.FromMinutes(5));

        Assert.Equal(Uri.UriSchemeHttps, signedUrl.Url.Scheme);
        Assert.Equal("api.cloudinary.com", signedUrl.Url.Host);
        Assert.Equal("/v1_1/demo-cloud/raw/download", signedUrl.Url.AbsolutePath);
        Assert.Contains("expires_at=", signedUrl.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public_id=medibridge/campaigns/file-id", signedUrl.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("signature=", signedUrl.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("type=authenticated", signedUrl.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_secret", signedUrl.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-hex-api-secret", signedUrl.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__cld_token__", signedUrl.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(signedUrl.ExpiresAtUtc, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(6));
    }
}
