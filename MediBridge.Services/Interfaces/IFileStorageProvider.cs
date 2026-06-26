using MediBridge.Core.Enums;

namespace MediBridge.Services.Interfaces;

public interface IFileStorageProvider
{
    Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default);
    Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default);
}

public sealed record FileStorageUploadRequest(
    string StorageKey,
    Stream Content,
    string ContentType,
    string Extension,
    StoredFileStorageResourceType ResourceType,
    StoredFileStorageDeliveryType DeliveryType,
    IReadOnlyDictionary<string, string> Context);

public sealed record FileStorageUploadResponse(
    string StorageProvider,
    string StorageKey,
    StoredFileStorageResourceType ResourceType,
    StoredFileStorageDeliveryType DeliveryType,
    long SizeBytes,
    string ContentType,
    string? VersionOrEtag);

public sealed record FileStorageAccessGrant(string Url, DateTime ExpiresAtUtc);
