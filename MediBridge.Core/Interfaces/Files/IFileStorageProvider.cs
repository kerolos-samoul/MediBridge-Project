namespace MediBridge.Core.Interfaces.Files;

public sealed record FileStorageUpload(
    string Folder,
    string OriginalFileName,
    string ContentType,
    long SizeBytes);

public sealed record FileStorageUploadResult(string StorageKey, string ResourceType);

public sealed record SignedFileUrl(Uri Url, DateTime ExpiresAtUtc);

public interface IFileStorageProvider
{
    Task<FileStorageUploadResult> UploadAsync(
        FileStorageUpload request,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<SignedFileUrl> CreateSignedReadUrlAsync(
        string storageKey,
        string resourceType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        string resourceType,
        CancellationToken cancellationToken = default);
}
