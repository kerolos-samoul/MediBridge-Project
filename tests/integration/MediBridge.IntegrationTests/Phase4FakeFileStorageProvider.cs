using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;

namespace MediBridge.IntegrationTests;

public sealed class Phase4FakeFileStorageProvider : IFileStorageProvider
{
    public int UploadCallCount { get; private set; }
    public int AccessGrantCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }
    public DateTime? AccessGrantExpiresAtOverride { get; set; }
    public List<FileStorageUploadRequest> UploadRequests { get; } = [];

    public Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default)
    {
        UploadCallCount++;
        UploadRequests.Add(request);

        return Task.FromResult(new FileStorageUploadResponse(
            "FakeStorage",
            request.StorageKey,
            request.ResourceType,
            request.DeliveryType,
            request.Content.Length,
            request.ContentType,
            $"etag-{UploadCallCount}"));
    }

    public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        AccessGrantCallCount++;
        return Task.FromResult(new FileStorageAccessGrant($"https://files.example.test/{Uri.EscapeDataString(storageKey)}", AccessGrantExpiresAtOverride ?? expiresAtUtc));
    }

    public Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default)
    {
        DeleteCallCount++;
        return Task.CompletedTask;
    }
}
