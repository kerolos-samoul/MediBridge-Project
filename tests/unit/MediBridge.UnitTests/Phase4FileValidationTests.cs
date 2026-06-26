using FluentAssertions;
using MediBridge.Core.Enums;
using MediBridge.Services.Config;
using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase4FileValidationTests
{
    [Fact]
    public void Phase4_file_lifecycle_enums_expose_required_values()
    {
        Enum.GetNames<FileReviewDecision>().Should().BeEquivalentTo(
            "Approved",
            "Rejected",
            "Quarantined",
            "ReplacementRequested",
            "Correction");

        Enum.GetNames<StoredFileUploadStatus>().Should().BeEquivalentTo(
            "PendingUpload",
            "Stored",
            "UploadFailed",
            "Deleted",
            "Replaced");

        Enum.GetNames<StoredFileSafetyScanStatus>().Should().BeEquivalentTo(
            "NotAvailable",
            "Pending",
            "Passed",
            "Failed",
            "Deferred");

        Enum.GetNames<StoredFileStorageResourceType>().Should().BeEquivalentTo("Image", "Video", "Raw");
        Enum.GetNames<StoredFileStorageDeliveryType>().Should().BeEquivalentTo("Private", "Authenticated");
        Enum.GetNames<FileAccessGrantOutcome>().Should().BeEquivalentTo("Issued", "Denied", "Expired");
    }

    [Fact]
    public void File_storage_options_defaults_match_phase4_limits_and_allow_lists()
    {
        var options = new FileStorageOptions();

        options.UploadsEnabled.Should().BeTrue();
        options.DocumentMaxBytes.Should().Be(10 * 1024 * 1024);
        options.ImageMaxBytes.Should().Be(10 * 1024 * 1024);
        options.AudioMaxBytes.Should().Be(25 * 1024 * 1024);
        options.VideoMaxBytes.Should().Be(100 * 1024 * 1024);
        options.AccessGrantLifetimeMinutes.Should().Be(10);
        options.AllowedDocumentContentTypes.Should().BeEquivalentTo(
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        options.AllowedImageContentTypes.Should().BeEquivalentTo("image/jpeg", "image/png");
        options.AllowedAudioContentTypes.Should().BeEquivalentTo("audio/mpeg");
        options.AllowedVideoContentTypes.Should().BeEquivalentTo("video/mp4");
        options.AllowedExtensions.Should().BeEquivalentTo(".pdf", ".docx", ".jpg", ".jpeg", ".png", ".mp3", ".mp4");

        options.Validate().Should().BeEmpty();
    }

    [Fact]
    public void File_storage_options_validation_requires_exact_phase4_values()
    {
        var options = new FileStorageOptions
        {
            DocumentMaxBytes = 1,
            ImageMaxBytes = 1,
            AudioMaxBytes = 1,
            VideoMaxBytes = 1,
            AccessGrantLifetimeMinutes = 1,
            AllowedDocumentContentTypes = ["application/pdf", "text/plain"],
            AllowedExtensions = [".pdf", ".txt"]
        };

        options.Validate().Should().Contain(error => error.Contains("DocumentMaxBytes", StringComparison.Ordinal));
        options.Validate().Should().Contain(error => error.Contains("ImageMaxBytes", StringComparison.Ordinal));
        options.Validate().Should().Contain(error => error.Contains("AudioMaxBytes", StringComparison.Ordinal));
        options.Validate().Should().Contain(error => error.Contains("VideoMaxBytes", StringComparison.Ordinal));
        options.Validate().Should().Contain(error => error.Contains("AccessGrantLifetimeMinutes", StringComparison.Ordinal));
        options.Validate().Should().Contain(error => error.Contains("content types", StringComparison.Ordinal));
        options.Validate().Should().Contain(error => error.Contains("extensions", StringComparison.Ordinal));
    }

    [Fact]
    public void Cloudinary_options_validation_requires_secret_when_uploads_are_enabled()
    {
        var options = new CloudinaryStorageOptions();

        options.Validate(uploadEnabled: true, cloudinaryUrl: null)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("CLOUDINARY_URL");

        options.Validate(uploadEnabled: true, cloudinaryUrl: "configured-cloudinary-url")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Cloudinary_options_resolves_secret_from_configuration_with_environment_precedence()
    {
        var previous = Environment.GetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName, null);
            var configuredOnly = new CloudinaryStorageOptions
            {
                CloudinaryUrl = "cloudinary" + "://config-key:config-secret@example"
            };

            configuredOnly.ResolveCloudinaryUrl().Should().Be("cloudinary" + "://config-key:config-secret@example");

            Environment.SetEnvironmentVariable(
                CloudinaryStorageOptions.SecretEnvironmentVariableName,
                "cloudinary" + "://env-key:env-secret@example");

            configuredOnly.ResolveCloudinaryUrl().Should().Be("cloudinary" + "://env-key:env-secret@example");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName, previous);
        }
    }

    [Fact]
    public async Task Cloudinary_provider_creates_signed_time_limited_private_download_grant()
    {
        var previous = Environment.GetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName, "cloudinary" + "://test-key:test-token@example");
            var provider = new CloudinaryFileStorageProvider(new CloudinaryStorageOptions { FolderPrefix = "medibridge" });
            var expiresAtUtc = DateTime.UtcNow.AddMinutes(10);

            var grant = await provider.CreatePrivateAccessGrantAsync("verification/doctor/file.pdf", StoredFileStorageResourceType.Raw, expiresAtUtc);

            grant.ExpiresAtUtc.Should().Be(expiresAtUtc);
            grant.Url.Should().Contain("expires_at=");
            grant.Url.Should().Contain("signature=");
            grant.Url.Should().Contain("public_id=");
            grant.Url.Should().NotContain("__cld_token__");
            grant.Url.Should().NotContain("api_secret=");
            grant.Url.Should().NotContain("test-token");
            grant.Url.ToLowerInvariant().Should().NotContain("/raw/private/verification/doctor/file.pdf");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName, previous);
        }
    }

    [Fact]
    public async Task Cloudinary_provider_does_not_duplicate_folder_prefix_for_persisted_public_ids()
    {
        var previous = Environment.GetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName, "cloudinary" + "://test-key:test-token@example");
            var provider = new CloudinaryFileStorageProvider(new CloudinaryStorageOptions { FolderPrefix = "medibridge" });

            var grant = await provider.CreatePrivateAccessGrantAsync(
                "medibridge/verification/doctor/file.pdf",
                StoredFileStorageResourceType.Raw,
                DateTime.UtcNow.AddMinutes(10));

            var publicId = System.Web.HttpUtility.ParseQueryString(new Uri(grant.Url).Query)["public_id"];

            publicId.Should().Be("medibridge/verification/doctor/file.pdf");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudinaryStorageOptions.SecretEnvironmentVariableName, previous);
        }
    }
}
