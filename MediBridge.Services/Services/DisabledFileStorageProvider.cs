using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class DisabledFileStorageProvider : IFileStorageProvider
{
    public Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default)
    {
        throw CreateDisabledException();
    }

    public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        throw CreateDisabledException();
    }

    public Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default)
    {
        throw CreateDisabledException();
    }

    private static InvalidOperationException CreateDisabledException()
    {
        return new InvalidOperationException("File storage is disabled by configuration.");
    }
}
