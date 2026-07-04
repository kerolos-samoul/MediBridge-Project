using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7DeliveryAssetAccessTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DeliveryAssetAccess_RequiresAuthentication()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/doctor/messages/delivery-1/assets/file-1/access");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeliveryAssetAccess_ValidatesCurrentDeliveryAndApprovedFile_ThenIssuesTenMinuteAuditedGrant()
    {
        var storage = new Phase4FakeFileStorageProvider();
        await using var factory = new AssetFactory(UtcNow, storage);
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", seed.UserId));

        using var response = await client.GetAsync($"/api/doctor/messages/{seed.DeliveryId}/assets/{seed.FileId}/access");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, storage.AccessGrantCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.StartsWith("https://files.example.test/", data.GetProperty("AccessUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal(UtcNow.AddMinutes(10), data.GetProperty("ExpiresAtUtc").GetDateTime());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var audit = await db.FileAccessGrantAudits.SingleAsync(item => item.StoredFileId == seed.FileId);
        Assert.Equal(FileAccessGrantOutcome.Issued, audit.Outcome);
        Assert.Equal(UtcNow.AddMinutes(10), audit.ExpiresAtUtc);
        Assert.Contains(await db.AuditEvents.Select(item => item.EventType).ToListAsync(), type => type == "DeliveryAssetAccessIssued");

        using var priorDay = await client.GetAsync($"/api/doctor/messages/{seed.PriorDayDeliveryId}/assets/{seed.FileId}/access");
        using var unrelated = await client.GetAsync($"/api/doctor/messages/{seed.DeliveryId}/assets/missing/access");
        Assert.Equal(HttpStatusCode.NotFound, priorDay.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unrelated.StatusCode);
        Assert.Equal(1, storage.AccessGrantCallCount);
    }

    [Fact]
    public async Task DeliveryAssetAccess_WhenProviderFails_ReturnsSafe503Envelope()
    {
        await using var factory = new AssetFactory(UtcNow, new FailingStorageProvider());
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", seed.UserId));

        using var response = await client.GetAsync($"/api/doctor/messages/{seed.DeliveryId}/assets/{seed.FileId}/access");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("provider-diagnostic-secret", payload, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(503, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task DeliveryAssetAccess_WhenProviderTimesOutWithoutCallerCancellation_ReturnsSafe503Envelope()
    {
        await using var factory = new AssetFactory(UtcNow, new TimeoutStorageProvider());
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", seed.UserId));

        using var response = await client.GetAsync($"/api/doctor/messages/{seed.DeliveryId}/assets/{seed.FileId}/access");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(503, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task DeliveryAssetAccess_WhenCallerCancelsDuringProviderCall_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await using var factory = new AssetFactory(UtcNow, new CallerCancellationStorageProvider(cancellation));
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAsync(factory.Services);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDoctorMessageService>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateDeliveryAssetAccessGrantAsync(
            seed.UserId,
            seed.DeliveryId,
            seed.FileId,
            cancellation.Token));
    }

    [Fact]
    public async Task DeliveryAssetAccess_DeniesCrossDoctorInactiveDoctorAndUnavailableAssetsWithoutStorageCalls()
    {
        var storage = new Phase4FakeFileStorageProvider();
        await using var factory = new AssetFactory(UtcNow, storage);
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAsync(factory.Services);
        var suffix = Guid.NewGuid().ToString("N");
        var otherDoctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            factory.Services, $"other-user-{suffix}", $"other-doctor-{suffix}", $"other-{suffix}@example.test", 50m, 10, UtcNow.AddDays(-10));
        var unavailableFileIds = await SeedUnavailableFilesAsync(factory.Services, seed, suffix);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", seed.UserId));
        foreach (var fileId in unavailableFileIds)
        {
            using var denied = await client.GetAsync($"/api/doctor/messages/{seed.DeliveryId}/assets/{fileId}/access");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            await AssertSafeEmptyEnvelopeAsync(denied, 404);
        }

        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", otherDoctor.UserId));
        using var crossDoctor = await otherClient.GetAsync($"/api/doctor/messages/{seed.DeliveryId}/assets/{seed.FileId}/access");
        Assert.Equal(HttpStatusCode.NotFound, crossDoctor.StatusCode);
        await AssertSafeEmptyEnvelopeAsync(crossDoctor, 404);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var user = await db.Users.SingleAsync(item => item.Id == seed.UserId);
            user.AccountStatus = AccountStatus.Suspended;
            await db.SaveChangesAsync();
        }

        using var inactiveDoctor = await client.GetAsync($"/api/doctor/messages/{seed.DeliveryId}/assets/{seed.FileId}/access");
        Assert.Equal(HttpStatusCode.Forbidden, inactiveDoctor.StatusCode);
        await AssertSafeEmptyEnvelopeAsync(inactiveDoctor, 403);
        Assert.Equal(0, storage.AccessGrantCallCount);
    }

    private static async Task<AssetSeed> SeedAsync(IServiceProvider services)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var doctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            services, $"user-{suffix}", $"doctor-{suffix}", $"doctor-{suffix}@example.test", 50m, 10, UtcNow.AddDays(-10));
        var company = await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            services, $"company-user-{suffix}", $"company-{suffix}", $"company-{suffix}@example.test", UtcNow.AddDays(-10));
        var campaignId = $"campaign-{suffix}";
        var fileId = $"file-{suffix}";
        var deliveryId = $"delivery-{suffix}";
        var priorDayDeliveryId = $"prior-{suffix}";
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            services, campaignId, company.CompanyId, CampaignStatus.Approved, UtcNow.AddDays(-2), UtcNow.AddDays(-3), false, null);
        await Phase7DeliveryTestHelpers.SeedApprovedCampaignAssetAsync(
            services, fileId, campaignId, company.CompanyId, StoredFilePurpose.CampaignMedia, $"private/{fileId}", UtcNow.AddDays(-2), UtcNow.AddDays(-1));
        await Phase7DeliveryTestHelpers.SeedDeliveryAsync(
            services, deliveryId, doctor.DoctorId, campaignId, company.CompanyId, new DateOnly(2026, 7, 3), UtcNow.AddMinutes(-10),
            DeliveryStatus.Active, ReservationStatus.Reserved, 50m, 20m, 10m, 40m, 50m, UtcNow.AddMinutes(-10));
        await Phase7DeliveryTestHelpers.SeedDeliveryAsync(
            services, priorDayDeliveryId, doctor.DoctorId, campaignId, company.CompanyId, new DateOnly(2026, 7, 2), UtcNow.AddDays(-1),
            DeliveryStatus.Active, ReservationStatus.Reserved, 50m, 20m, 10m, 40m, 50m, UtcNow.AddDays(-1));
        return new AssetSeed(doctor.UserId, deliveryId, priorDayDeliveryId, fileId);
    }

    private static async Task<IReadOnlyList<string>> SeedUnavailableFilesAsync(
        IServiceProvider services,
        AssetSeed seed,
        string suffix)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var source = await db.StoredFiles.SingleAsync(item => item.Id == seed.FileId);
        var unrelatedCampaignId = $"unrelated-campaign-{suffix}";
        var companyId = source.OwnerId;
        db.Campaigns.Add(new MediBridge.Core.Entities.Campaigns.Campaign
        {
            Id = unrelatedCampaignId,
            CompanyId = companyId,
            Title = "Unrelated campaign",
            Description = "Asset authorization boundary",
            Status = CampaignStatus.Approved,
            SubmittedAtUtc = UtcNow.AddDays(-2),
            CreatedAtUtc = UtcNow.AddDays(-3)
        });

        var replacementId = $"replacement-{suffix}";
        var files = new[]
        {
            CreateFile($"pending-{suffix}", source.RelatedCampaignId!, companyId, StoredFileReviewStatus.Pending),
            CreateFile($"rejected-{suffix}", source.RelatedCampaignId!, companyId, StoredFileReviewStatus.Rejected),
            CreateFile($"quarantined-{suffix}", source.RelatedCampaignId!, companyId, StoredFileReviewStatus.Quarantined),
            CreateFile($"deleted-{suffix}", source.RelatedCampaignId!, companyId, StoredFileReviewStatus.Approved, deletedAtUtc: UtcNow.AddMinutes(-1)),
            CreateFile(replacementId, source.RelatedCampaignId!, companyId, StoredFileReviewStatus.Approved),
            CreateFile($"replaced-{suffix}", source.RelatedCampaignId!, companyId, StoredFileReviewStatus.Approved, replacedByFileId: replacementId),
            CreateFile($"unrelated-{suffix}", unrelatedCampaignId, companyId, StoredFileReviewStatus.Approved)
        };
        db.StoredFiles.AddRange(files);
        await db.SaveChangesAsync();
        return files.Where(file => file.Id != replacementId).Select(file => file.Id).ToArray();
    }

    private static StoredFile CreateFile(
        string id,
        string campaignId,
        string companyId,
        StoredFileReviewStatus reviewStatus,
        DateTime? deletedAtUtc = null,
        string? replacedByFileId = null)
    {
        return new StoredFile
        {
            Id = id,
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = companyId,
            RelatedCampaignId = campaignId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = $"{id}.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            StorageKey = $"private/{id}",
            StorageProvider = "TestStorage",
            StorageResourceType = StoredFileStorageResourceType.Raw,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = reviewStatus,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Passed,
            CreatedAtUtc = UtcNow.AddDays(-2),
            ReviewedAtUtc = reviewStatus == StoredFileReviewStatus.Pending ? null : UtcNow.AddDays(-1),
            DeletedAtUtc = deletedAtUtc,
            ReplacedByFileId = replacedByFileId
        };
    }

    private static async Task AssertSafeEmptyEnvelopeAsync(HttpResponseMessage response, int expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("Message").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private sealed record AssetSeed(string UserId, string DeliveryId, string PriorDayDeliveryId, string FileId);

    private sealed class AssetFactory(DateTime utcNow, IFileStorageProvider storageProvider) : ConfiguredWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(utcNow));
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton(storageProvider);
            });
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FailingStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("provider-diagnostic-secret");
    }

    private sealed class TimeoutStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException("Synthetic provider timeout.");
    }

    private sealed class CallerCancellationStorageProvider(CancellationTokenSource cancellation) : IFileStorageProvider
    {
        public Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
