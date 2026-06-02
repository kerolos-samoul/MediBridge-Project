using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3StoredFileMetadataTests
{
    [Fact]
    public async Task StoredFileRepository_Persists_MetadataOnlyWithoutUploadBehavior()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var storedFileId = $"file-{Guid.NewGuid():N}";

        await unitOfWork.StoredFiles.AddStoredFileAsync(storedFileId, StoredFileOwnerType.Company, ownerId, StoredFilePurpose.VerificationDocument);
        await unitOfWork.SaveChangesAsync();

        var storedFile = await context.StoredFiles.SingleAsync(file => file.Id == storedFileId);
        Assert.Equal(StoredFileOwnerType.Company, storedFile.OwnerType);
        Assert.Equal(ownerId, storedFile.OwnerId);
        Assert.Equal(StoredFilePurpose.VerificationDocument, storedFile.Purpose);
        Assert.Equal(StoredFileVisibility.Private, storedFile.Visibility);
        Assert.Equal(StoredFileReviewStatus.Pending, storedFile.ReviewStatus);
        Assert.False(string.IsNullOrWhiteSpace(storedFile.StorageKey));
        Assert.Contains(storedFileId, await unitOfWork.StoredFiles.ListActiveStoredFileIdsByOwnerAsync(StoredFileOwnerType.Company, ownerId));
    }
}
