using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase4CampaignFileIntegrationTests
{
    [Fact]
    public async Task CampaignAssetCompatibilityUpload_DelegatesToCurrentFileWorkflow()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = await client.PostAsync(
            $"/api/company/campaigns/{campaignId}/assets",
            CreateReplacementMultipart());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, storageProvider.UploadCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            StoredFilePurpose.CampaignMedia.ToString(),
            document.RootElement.GetProperty("Data").GetProperty("Purpose").GetString());
    }

    [Theory]
    [InlineData("replace", HttpStatusCode.Created)]
    [InlineData("delete", HttpStatusCode.NoContent)]
    public async Task CampaignAssetCompatibilityChanges_DelegateToCurrentFileWorkflow(
        string operation,
        HttpStatusCode expectedStatus)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id);
        var fileId = await SeedOwnedCampaignFileAsync(factory.Services, user.Id, campaignId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = operation == "replace"
            ? await client.PostAsync(
                $"/api/company/campaigns/{campaignId}/assets/{fileId}/replacement",
                CreateReplacementMultipart())
            : await client.DeleteAsync($"/api/company/campaigns/{campaignId}/assets/{fileId}");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(operation == "replace" ? 1 : 0, storageProvider.UploadCallCount);
        Assert.Equal(operation == "delete" ? 1 : 0, storageProvider.DeleteCallCount);
    }

    [Fact]
    public async Task FileAccessCompatibilityGet_DelegatesToCurrentPrivateAccessWorkflow()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id);
        var fileId = await SeedOwnedCampaignFileAsync(
            factory.Services,
            user.Id,
            campaignId,
            StoredFileReviewStatus.Approved);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = await client.GetAsync($"/api/files/{fileId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, storageProvider.AccessGrantCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith(
            "https://files.example.test/",
            document.RootElement.GetProperty("Data").GetProperty("Url").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminCampaignAssetReviewCompatibilityPost_DelegatesToCurrentFileReviewWorkflow()
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var companyEmail = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, companyEmail, AccountStatus.Approved);
        var company = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, companyEmail);
        var campaignId = await SeedCampaignAsync(factory.Services, company.Id);
        var fileId = await SeedOwnedCampaignFileAsync(factory.Services, company.Id, campaignId);
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PostAsJsonAsync(
            $"/api/admin/campaign-assets/{fileId}/review",
            new { Decision = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var storedFile = await db.StoredFiles.SingleAsync(file => file.Id == fileId);
        Assert.Equal(StoredFileReviewStatus.Approved, storedFile.ReviewStatus);
        Assert.Equal(admin.Id, storedFile.ReviewedByAdminId);
    }

    [Theory]
    [InlineData(StoredFilePurpose.CampaignMedia, "banner.png", "image/png", 1024)]
    [InlineData(StoredFilePurpose.VoiceNote, "voice.mp3", "audio/mpeg", 1024)]
    [InlineData(StoredFilePurpose.CampaignMedia, "video.mp4", "video/mp4", 1024)]
    [InlineData(StoredFilePurpose.ClinicalResearchAttachment, "study.pdf", "application/pdf", 1024)]
    public async Task CampaignFileUpload_WithOwnedCampaignAndValidFile_CreatesPrivatePendingCampaignMetadata(
        StoredFilePurpose purpose,
        string fileName,
        string contentType,
        int sizeBytes)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart(purpose.ToString(), fileName, contentType, sizeBytes));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, storageProvider.UploadCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        var fileId = data.GetProperty("Id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(fileId));
        Assert.Equal(purpose.ToString(), data.GetProperty("Purpose").GetString());
        Assert.Equal("Company", data.GetProperty("OwnerType").GetString());
        Assert.Equal(campaignId, data.GetProperty("RelatedCampaignId").GetString());
        Assert.Equal("Pending", data.GetProperty("ReviewStatus").GetString());
        Assert.Equal("Stored", data.GetProperty("UploadStatus").GetString());
        Assert.Equal("Deferred", data.GetProperty("SafetyScanStatus").GetString());
        AssertDtoHasNoPrivateStorageFields(data);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var storedFile = await db.StoredFiles.SingleAsync(file => file.Id == fileId);
        var companyId = await db.CompanyProfiles.Where(profile => profile.UserId == user.Id).Select(profile => profile.Id).SingleAsync();
        Assert.Equal(StoredFileOwnerType.Company, storedFile.OwnerType);
        Assert.Equal(companyId, storedFile.OwnerId);
        Assert.Equal(campaignId, storedFile.RelatedCampaignId);
        Assert.Equal(purpose, storedFile.Purpose);
        Assert.Equal(StoredFileVisibility.Private, storedFile.Visibility);
        Assert.Equal(StoredFileUploadStatus.Stored, storedFile.UploadStatus);
        Assert.Equal(StoredFileReviewStatus.Pending, storedFile.ReviewStatus);
        Assert.NotEqual(fileName, storedFile.StorageKey);
        Assert.StartsWith("campaigns/", storedFile.StorageKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CampaignFileUpload_WithNonOwnerCompanyOrNonCompanyRole_IsDenied()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        var otherCompanyEmail = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        var doctorEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var otherCompany = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, otherCompanyEmail);
        var doctor = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, doctorEmail);
        var campaignId = await SeedCampaignAsync(factory.Services, owner.Id);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", otherCompany.Id));
        using var nonOwnerResponse = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("CampaignMedia", "banner.png", "image/png", 1024));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.Id));
        using var doctorResponse = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("CampaignMedia", "banner.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.Forbidden, nonOwnerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, doctorResponse.StatusCode);
        Assert.Equal(0, storageProvider.UploadCallCount);
    }

    [Fact]
    public async Task CampaignFileUpload_WithOwnedRevisionRequiredCampaign_IsAllowed()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id, CampaignStatus.RevisionRequired);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = await client.PostAsync(
            $"/api/campaigns/{campaignId}/files",
            CreateMultipart("CampaignMedia", "revision.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, storageProvider.UploadCallCount);
    }

    [Theory]
    [InlineData(CampaignStatus.RevisionRequired, "replace", HttpStatusCode.Created)]
    [InlineData(CampaignStatus.RevisionRequired, "delete", HttpStatusCode.OK)]
    [InlineData(CampaignStatus.Rejected, "replace", HttpStatusCode.Forbidden)]
    [InlineData(CampaignStatus.Rejected, "delete", HttpStatusCode.Forbidden)]
    public async Task CampaignFileChanges_RequireDraftOrRevisionRequiredCampaign(
        CampaignStatus status,
        string operation,
        HttpStatusCode expectedStatus)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id, status);
        var fileId = await SeedOwnedCampaignFileAsync(factory.Services, user.Id, campaignId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        var response = operation == "replace"
            ? await client.PostAsync($"/api/files/{fileId}/replacement", CreateReplacementMultipart())
            : await client.DeleteAsync($"/api/files/{fileId}");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedStatus == HttpStatusCode.Created ? 1 : 0, storageProvider.UploadCallCount);
        Assert.Equal(expectedStatus == HttpStatusCode.OK ? 1 : 0, storageProvider.DeleteCallCount);
    }

    [Theory]
    [InlineData(CampaignStatus.PendingReview)]
    [InlineData(CampaignStatus.Rejected)]
    [InlineData(CampaignStatus.Approved)]
    [InlineData(CampaignStatus.Active)]
    [InlineData(CampaignStatus.Completed)]
    public async Task CampaignFileUpload_WithOwnedNonDraftCampaign_IsDeniedBeforeProviderUpload(CampaignStatus status)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id, status);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("CampaignMedia", "banner.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, storageProvider.UploadCallCount);
    }

    [Theory]
    [InlineData(StoredFilePurpose.ClinicalResearchAttachment, "oversize.pdf", "application/pdf", 10 * 1024 * 1024 + 1)]
    [InlineData(StoredFilePurpose.CampaignMedia, "oversize.png", "image/png", 10 * 1024 * 1024 + 1)]
    [InlineData(StoredFilePurpose.VoiceNote, "oversize.mp3", "audio/mpeg", 25 * 1024 * 1024 + 1)]
    [InlineData(StoredFilePurpose.CampaignMedia, "oversize.mp4", "video/mp4", 100 * 1024 * 1024 + 1)]
    [InlineData(StoredFilePurpose.CampaignMedia, "banner.png", "text/plain", 1024)]
    [InlineData(StoredFilePurpose.CampaignMedia, "banner.exe", "image/png", 1024)]
    [InlineData(StoredFilePurpose.CampaignMedia, "banner.png", "image/jpeg", 1024)]
    [InlineData(StoredFilePurpose.CampaignMedia, "../banner.png", "image/png", 1024)]
    public async Task CampaignFileUpload_WithInvalidFile_IsRejectedBeforeProviderUpload(
        StoredFilePurpose purpose,
        string fileName,
        string contentType,
        int sizeBytes)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var campaignId = await SeedCampaignAsync(factory.Services, user.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart(purpose.ToString(), fileName, contentType, sizeBytes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, storageProvider.UploadCallCount);
    }

    [Theory]
    [InlineData(StoredFileReviewStatus.Approved, StoredFileUploadStatus.Stored, false, true)]
    [InlineData(StoredFileReviewStatus.Rejected, StoredFileUploadStatus.Stored, false, false)]
    [InlineData(StoredFileReviewStatus.Quarantined, StoredFileUploadStatus.Stored, false, false)]
    [InlineData(StoredFileReviewStatus.Approved, StoredFileUploadStatus.Deleted, true, false)]
    [InlineData(StoredFileReviewStatus.Approved, StoredFileUploadStatus.Replaced, false, false)]
    [InlineData(StoredFileReviewStatus.Pending, StoredFileUploadStatus.Stored, false, false)]
    public async Task CampaignReadiness_ChecksOnlyApprovedStoredAvailableAssets(
        StoredFileReviewStatus reviewStatus,
        StoredFileUploadStatus uploadStatus,
        bool deleted,
        bool expectedAvailable)
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        var fileId = await SeedCampaignStoredFileAsync(factory.Services, reviewStatus, uploadStatus, deleted);

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var available = await unitOfWork.StoredFiles.IsAvailableAsApprovedAssetAsync(fileId);
        var missing = await unitOfWork.StoredFiles.IsAvailableAsApprovedAssetAsync(Guid.NewGuid().ToString("N"));

        Assert.Equal(expectedAvailable, available);
        Assert.False(missing);
    }

    private static Phase4CampaignFileWebAppFactory CreateFactory(out Phase4FakeFileStorageProvider storageProvider)
    {
        storageProvider = new Phase4FakeFileStorageProvider();
        return new Phase4CampaignFileWebAppFactory(storageProvider);
    }

    private static MultipartFormDataContent CreateMultipart(string purpose, string fileName, string contentType, int sizeBytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(purpose), "Purpose");
        var bytes = sizeBytes <= 0 ? [] : new byte[sizeBytes];
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "File", fileName);
        return content;
    }

    private static MultipartFormDataContent CreateReplacementMultipart()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[1024]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(fileContent, "File", "replacement.png");
        return content;
    }

    private static async Task<string> SeedCampaignAsync(IServiceProvider services, string companyUserId, CampaignStatus status = CampaignStatus.Draft)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var companyId = await db.CompanyProfiles.Where(profile => profile.UserId == companyUserId).Select(profile => profile.Id).SingleAsync();
        var campaign = new Campaign
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = companyId,
            Title = "Campaign draft",
            Description = "Campaign file upload test",
            Status = status
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    private static async Task<string> SeedCampaignStoredFileAsync(
        IServiceProvider services,
        StoredFileReviewStatus reviewStatus,
        StoredFileUploadStatus uploadStatus,
        bool deleted)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = $"company-{Guid.NewGuid():N}",
            RelatedCampaignId = $"campaign-{Guid.NewGuid():N}",
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "asset.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{Guid.NewGuid():N}/asset.png",
            StorageProvider = "FakeStorage",
            StorageResourceType = StoredFileStorageResourceType.Image,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = reviewStatus,
            UploadStatus = uploadStatus,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null,
            CreatedAtUtc = DateTime.UtcNow
        };
        if (uploadStatus == StoredFileUploadStatus.Replaced)
        {
            file.ReplacedByFileId = Guid.NewGuid().ToString("N");
            db.StoredFiles.Add(new StoredFile
            {
                Id = file.ReplacedByFileId,
                OwnerType = file.OwnerType,
                OwnerId = file.OwnerId,
                RelatedCampaignId = file.RelatedCampaignId,
                Purpose = file.Purpose,
                OriginalFileName = "replacement.png",
                ContentType = "image/png",
                SizeBytes = 1024,
                StorageKey = $"campaigns/{Guid.NewGuid():N}/replacement.png",
                StorageProvider = "FakeStorage",
                StorageResourceType = StoredFileStorageResourceType.Image,
                StorageDeliveryType = StoredFileStorageDeliveryType.Private,
                Visibility = StoredFileVisibility.Private,
                ReviewStatus = StoredFileReviewStatus.Pending,
                UploadStatus = StoredFileUploadStatus.Stored,
                SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static async Task<string> SeedOwnedCampaignFileAsync(
        IServiceProvider services,
        string companyUserId,
        string campaignId,
        StoredFileReviewStatus reviewStatus = StoredFileReviewStatus.Rejected)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var companyId = await db.CompanyProfiles
            .Where(profile => profile.UserId == companyUserId)
            .Select(profile => profile.Id)
            .SingleAsync();
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = companyId,
            RelatedCampaignId = campaignId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "rejected.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{campaignId}/rejected.png",
            StorageProvider = "FakeStorage",
            StorageResourceType = StoredFileStorageResourceType.Image,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = reviewStatus,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = DateTime.UtcNow
        };
        await db.StoredFiles.AddAsync(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static void AssertDtoHasNoPrivateStorageFields(JsonElement data)
    {
        foreach (var property in data.EnumerateObject())
        {
            Assert.DoesNotContain("StorageKey", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Credential", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Signed", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Token", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Diagnostic", property.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class Phase4CampaignFileWebAppFactory : TestHost.ConfiguredWebAppFactory
    {
        private readonly Phase4FakeFileStorageProvider storageProvider;

        public Phase4CampaignFileWebAppFactory(Phase4FakeFileStorageProvider storageProvider)
        {
            this.storageProvider = storageProvider;
        }

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton<IFileStorageProvider>(storageProvider);
            });
        }
    }
}
