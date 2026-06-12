using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using MediBridge.Core.Enums;
using MediBridge.Services.Config;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediBridge.Services.Services;

public sealed class CloudinaryFileStorageProvider : IFileStorageProvider
{
    private readonly CloudinaryStorageOptions options;
    private readonly ILogger<CloudinaryFileStorageProvider> logger;
    private readonly string? cloudinaryUrl;

    public CloudinaryFileStorageProvider(CloudinaryStorageOptions options, ILogger<CloudinaryFileStorageProvider>? logger = null)
    {
        this.options = options;
        this.logger = logger ?? NullLogger<CloudinaryFileStorageProvider>.Instance;
        cloudinaryUrl = options.ResolveCloudinaryUrl();
    }

    public Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default)
    {
        return UploadCoreAsync(request, cancellationToken);
    }

    public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var cloudinary = CreateClient();
        var url = cloudinary.DownloadPrivate(
            ResolveProviderPublicId(storageKey),
            attachment: false,
            format: null,
            type: "private",
            expiresAt: new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds(),
            resourceType: MapResourceTypeName(resourceType),
            transformation: null,
            targetFilename: null);
        return Task.FromResult(new FileStorageAccessGrant(url, expiresAtUtc));
    }

    public Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default)
    {
        return DeleteCoreAsync(storageKey, resourceType);
    }

    private async Task<FileStorageUploadResponse> UploadCoreAsync(FileStorageUploadRequest request, CancellationToken cancellationToken)
    {
        var cloudinary = CreateClient();
        logger.LogInformation(
            "TEMP Cloudinary upload starting. StorageKey: {StorageKey}, ContentType: {ContentType}, ResourceType: {ResourceType}, DeliveryType: {DeliveryType}, LengthKnown: {LengthKnown}",
            request.StorageKey,
            request.ContentType,
            request.ResourceType,
            request.DeliveryType,
            request.Content.CanSeek);

        try
        {
            UploadResult result;
            if (request.ResourceType == StoredFileStorageResourceType.Image)
            {
                result = await cloudinary.UploadAsync(new ImageUploadParams
                {
                    File = new FileDescription(request.StorageKey, request.Content),
                    PublicId = BuildPublicId(request.StorageKey),
                    Type = MapDeliveryType(request.DeliveryType)
                }, cancellationToken);
            }
            else if (request.ResourceType == StoredFileStorageResourceType.Video)
            {
                result = await cloudinary.UploadAsync(new VideoUploadParams
                {
                    File = new FileDescription(request.StorageKey, request.Content),
                    PublicId = BuildPublicId(request.StorageKey),
                    Type = MapDeliveryType(request.DeliveryType)
                }, cancellationToken);
            }
            else
            {
                result = await cloudinary.UploadAsync(new RawUploadParams
                {
                    File = new FileDescription(request.StorageKey, request.Content),
                    PublicId = BuildPublicId(request.StorageKey),
                    Type = MapDeliveryType(request.DeliveryType)
                }, "upload", cancellationToken);
            }

            logger.LogInformation(
                "TEMP Cloudinary upload succeeded. StorageKey: {StorageKey}, PublicId: {PublicId}, Bytes: {Bytes}, Version: {Version}, SecureUrlPresent: {SecureUrlPresent}",
                request.StorageKey,
                result.PublicId,
                result.Bytes,
                result.Version,
                result.SecureUrl is not null);

            return new FileStorageUploadResponse(
                "Cloudinary",
                result.PublicId ?? request.StorageKey,
                request.ResourceType,
                request.DeliveryType,
                result.Bytes,
                request.ContentType,
                result.Version);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "TEMP Cloudinary upload failed. StorageKey: {StorageKey}, ContentType: {ContentType}, ResourceType: {ResourceType}, DeliveryType: {DeliveryType}",
                request.StorageKey,
                request.ContentType,
                request.ResourceType,
                request.DeliveryType);
            throw new InvalidOperationException("Storage provider operation failed.");
        }
    }

    private async Task DeleteCoreAsync(string storageKey, StoredFileStorageResourceType resourceType)
    {
        var cloudinary = CreateClient();
        try
        {
            await cloudinary.DestroyAsync(new DeletionParams(ResolveProviderPublicId(storageKey))
            {
                ResourceType = MapResourceType(resourceType),
                Type = "private",
                Invalidate = true
            });
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Storage provider operation failed.");
        }
    }

    private Cloudinary CreateClient()
    {
        var errors = options.Validate(uploadEnabled: true, cloudinaryUrl);
        if (errors.Count > 0)
        {
            logger.LogError(
                "TEMP Cloudinary configuration validation failed. CloudinaryUrlConfigured: {CloudinaryUrlConfigured}, ConfigCloudName: {ConfigCloudName}, UseSecureUrls: {UseSecureUrls}, FolderPrefix: {FolderPrefix}, Errors: {Errors}",
                !string.IsNullOrWhiteSpace(cloudinaryUrl),
                options.CloudName,
                options.UseSecureUrls,
                options.FolderPrefix,
                string.Join(" ", errors));
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        logger.LogInformation(
            "TEMP Cloudinary client created. CloudinaryUrlConfigured: {CloudinaryUrlConfigured}, ConfigCloudName: {ConfigCloudName}, UrlCloudName: {UrlCloudName}, UseSecureUrls: {UseSecureUrls}, FolderPrefix: {FolderPrefix}",
            !string.IsNullOrWhiteSpace(cloudinaryUrl),
            options.CloudName,
            TryReadCloudName(cloudinaryUrl),
            options.UseSecureUrls,
            options.FolderPrefix);

        var cloudinary = new Cloudinary(cloudinaryUrl)
        {
            Api =
            {
                Secure = options.UseSecureUrls
            }
        };
        return cloudinary;
    }

    private static string? TryReadCloudName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;
    }

    private string BuildPublicId(string storageKey)
    {
        var normalizedPrefix = options.FolderPrefix.Trim().Trim('/');
        var normalizedKey = storageKey.Trim().Trim('/');
        return string.IsNullOrWhiteSpace(normalizedPrefix)
            ? normalizedKey
            : $"{normalizedPrefix}/{normalizedKey}";
    }

    private string ResolveProviderPublicId(string storageKey)
    {
        var normalizedPrefix = options.FolderPrefix.Trim().Trim('/');
        var normalizedKey = storageKey.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPrefix) ||
            normalizedKey.StartsWith($"{normalizedPrefix}/", StringComparison.Ordinal))
        {
            return normalizedKey;
        }

        return $"{normalizedPrefix}/{normalizedKey}";
    }

    private static string MapDeliveryType(StoredFileStorageDeliveryType deliveryType)
    {
        return deliveryType == StoredFileStorageDeliveryType.Authenticated ? "authenticated" : "private";
    }

    private static ResourceType MapResourceType(StoredFileStorageResourceType resourceType)
    {
        return resourceType switch
        {
            StoredFileStorageResourceType.Image => ResourceType.Image,
            StoredFileStorageResourceType.Video => ResourceType.Video,
            _ => ResourceType.Raw
        };
    }

    private static string MapResourceTypeName(StoredFileStorageResourceType resourceType)
    {
        return resourceType switch
        {
            StoredFileStorageResourceType.Image => "image",
            StoredFileStorageResourceType.Video => "video",
            _ => "raw"
        };
    }
}
