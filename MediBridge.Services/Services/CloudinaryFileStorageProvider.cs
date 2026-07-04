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
            "Private file upload starting. ContentType: {ContentType}, ResourceType: {ResourceType}, DeliveryType: {DeliveryType}, LengthKnown: {LengthKnown}",
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
                "Private file upload succeeded. Bytes: {Bytes}, Version: {Version}",
                result.Bytes,
                result.Version);

            return new FileStorageUploadResponse(
                "Cloudinary",
                result.PublicId ?? request.StorageKey,
                request.ResourceType,
                request.DeliveryType,
                result.Bytes,
                request.ContentType,
                result.Version);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                "Private file upload failed. ContentType: {ContentType}, ResourceType: {ResourceType}, DeliveryType: {DeliveryType}",
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
                "Private storage configuration validation failed with {ErrorCount} safe validation errors.",
                errors.Count);
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        logger.LogDebug("Private storage client created using secure URLs: {UseSecureUrls}.", options.UseSecureUrls);

        var cloudinary = new Cloudinary(cloudinaryUrl)
        {
            Api =
            {
                Secure = options.UseSecureUrls
            }
        };
        return cloudinary;
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
