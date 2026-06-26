using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using MediBridge.Core.Interfaces.Files;
using Microsoft.Extensions.Options;
using System.Net;

namespace MediBridge.Repository.Storage;

public sealed class CloudinaryFileStorageProvider : IFileStorageProvider
{
    private const string AuthenticatedStorageType = "authenticated";

    private readonly Cloudinary cloudinary;
    private readonly CloudinaryStorageOptions options;

    public CloudinaryFileStorageProvider(Cloudinary cloudinary, IOptions<CloudinaryStorageOptions> options)
    {
        this.cloudinary = cloudinary;
        this.options = options.Value;
        this.options.Validate();
    }

    public static string MapResourceType(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "image";
        }

        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return "video";
        }

        return "raw";
    }

    public static string BuildFolder(string folderPrefix, string folder)
    {
        var prefix = folderPrefix.Trim('/');
        var child = folder.Trim('/');

        return string.IsNullOrWhiteSpace(child)
            ? prefix
            : $"{prefix}/{child}";
    }

    public static string CreatePublicId(string originalFileName)
    {
        _ = originalFileName;
        return Guid.NewGuid().ToString("N");
    }

    public async Task<FileStorageUploadResult> UploadAsync(
        FileStorageUpload request,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!content.CanRead)
        {
            throw new ArgumentException("File content stream must be readable.", nameof(content));
        }

        if (request.SizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "File size must be positive.");
        }

        var resourceType = MapResourceType(request.ContentType);
        var folder = BuildFolder(options.FolderPrefix, request.Folder);
        var publicId = CreatePublicId(request.OriginalFileName);
        var file = new FileDescription(request.OriginalFileName, content);

        try
        {
            var result = resourceType switch
            {
                "image" => await cloudinary.UploadAsync(CreateImageUploadParams(folder, publicId, file), cancellationToken),
                "video" => await cloudinary.UploadAsync(CreateVideoUploadParams(folder, publicId, file), cancellationToken),
                _ => await cloudinary.UploadAsync(CreateRawUploadParams(folder, publicId, file), AuthenticatedStorageType, cancellationToken)
            };

            EnsureUploadSucceeded(result.PublicId, result.StatusCode, result.Error);

            return new FileStorageUploadResult(result.PublicId, resourceType);
        }
        catch (FileStorageUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FileStorageUnavailableException("File storage provider is unavailable.", ex);
        }
    }

    public Task<SignedFileUrl> CreateSignedReadUrlAsync(
        string storageKey,
        string resourceType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var boundedLifetime = TimeSpan.FromMinutes(Math.Min(lifetime.TotalMinutes, options.SignedUrlMinutes));
        if (boundedLifetime <= TimeSpan.Zero)
        {
            boundedLifetime = TimeSpan.FromMinutes(options.SignedUrlMinutes);
        }

        var expiresAtUtc = DateTime.UtcNow.Add(boundedLifetime);
        var expirationUnix = new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds();
        try
        {
            var url = cloudinary.DownloadPrivate(
                storageKey,
                attachment: null,
                format: null,
                type: AuthenticatedStorageType,
                expiresAt: expirationUnix,
                resourceType: NormalizeResourceType(resourceType),
                transformation: null,
                targetFilename: null);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var signedUri)
                || (options.UseSecureUrls && signedUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new FileStorageUnavailableException("File storage provider did not return a secure signed URL.");
            }

            return Task.FromResult(new SignedFileUrl(signedUri, expiresAtUtc));
        }
        catch (FileStorageUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FileStorageUnavailableException("File storage provider is unavailable.", ex);
        }
    }

    public async Task DeleteAsync(
        string storageKey,
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await cloudinary.DestroyAsync(new DeletionParams(storageKey)
            {
                ResourceType = ToCloudinaryResourceType(resourceType),
                Type = AuthenticatedStorageType,
                Invalidate = true
            });

            if (IsNotFound(result.Result))
            {
                return;
            }

            if (result.Error is not null || (int)result.StatusCode >= 400)
            {
                throw new FileStorageUnavailableException("File storage provider did not complete deletion.");
            }
        }
        catch (FileStorageUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FileStorageUnavailableException("File storage provider is unavailable.", ex);
        }
    }

    private static ImageUploadParams CreateImageUploadParams(string folder, string publicId, FileDescription file) =>
        new()
        {
            File = file,
            Folder = folder,
            PublicId = publicId,
            Type = AuthenticatedStorageType,
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = false
        };

    private static VideoUploadParams CreateVideoUploadParams(string folder, string publicId, FileDescription file) =>
        new()
        {
            File = file,
            Folder = folder,
            PublicId = publicId,
            Type = AuthenticatedStorageType,
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = false
        };

    private static RawUploadParams CreateRawUploadParams(string folder, string publicId, FileDescription file) =>
        new()
        {
            File = file,
            Folder = folder,
            PublicId = publicId,
            Type = AuthenticatedStorageType,
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = false
        };

    private static void EnsureUploadSucceeded(string? publicId, HttpStatusCode statusCode, Error? error)
    {
        if (string.IsNullOrWhiteSpace(publicId) || error is not null || (int)statusCode >= 400)
        {
            throw new FileStorageUnavailableException("File storage provider did not accept the upload.");
        }
    }

    private static string NormalizeResourceType(string resourceType) =>
        resourceType.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? "image"
            : resourceType.Equals("video", StringComparison.OrdinalIgnoreCase)
                ? "video"
                : "raw";

    private static ResourceType ToCloudinaryResourceType(string resourceType) =>
        NormalizeResourceType(resourceType) switch
        {
            "image" => ResourceType.Image,
            "video" => ResourceType.Video,
            _ => ResourceType.Raw
        };

    private static bool IsNotFound(string? result) =>
        string.Equals(result, "not found", StringComparison.OrdinalIgnoreCase)
        || string.Equals(result, "not_found", StringComparison.OrdinalIgnoreCase);
}
