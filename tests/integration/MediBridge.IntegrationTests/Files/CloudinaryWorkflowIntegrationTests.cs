using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Files;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests.Files;

public sealed class CloudinaryWorkflowIntegrationTests
{
    [Fact]
    public async Task Replacement_ForPendingDraftAsset_CreatesNewAssetAndRetainsSupersededAsset()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        var campaignId = await CreateDraftAsync(client);
        var oldAssetId = await UploadAssetAsync(client, campaignId, "old.png", new byte[] { 1, 2, 3 });

        using var replacement = await UploadReplacementAsync(client, campaignId, oldAssetId, "new.png", new byte[] { 4, 5, 6 });

        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        var replacementId = await ReadAssetIdAsync(replacement);
        var oldAsset = await WalletCampaignWorkflowTestHelpers.FindAssetAsync(factory, oldAssetId);
        var newAsset = await WalletCampaignWorkflowTestHelpers.FindAssetAsync(factory, replacementId);
        Assert.NotNull(oldAsset);
        Assert.NotNull(newAsset);
        Assert.Equal(replacementId, oldAsset.SupersededByFileId);
        Assert.Equal(StoredFileReviewStatus.Pending, oldAsset.ReviewStatus);
        Assert.Equal(StorageObjectState.Active, oldAsset.StorageState);
        Assert.Equal(StorageObjectState.Active, newAsset.StorageState);
        Assert.Equal(campaignId, newAsset.OwnerId);
        Assert.NotEqual(oldAsset.StorageKey, newAsset.StorageKey);
    }

    [Fact]
    public async Task Delete_ForPendingDraftAsset_MarksDeletedAndIsIdempotent()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        var campaignId = await CreateDraftAsync(client);
        var assetId = await UploadAssetAsync(client, campaignId, "delete.png", new byte[] { 1, 2, 3 });

        using var firstDelete = await client.DeleteAsync(AssetRoute(campaignId, assetId));
        using var secondDelete = await client.DeleteAsync(AssetRoute(campaignId, assetId));

        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);
        var asset = await WalletCampaignWorkflowTestHelpers.FindAssetAsync(factory, assetId);
        Assert.NotNull(asset);
        Assert.Equal(StorageObjectState.Deleted, asset.StorageState);
        Assert.NotNull(asset.DeletedAtUtc);
    }

    [Fact]
    public async Task Delete_WhenProviderFails_LeavesDeletionPendingAndSignedAccessReturnsNotFound()
    {
        await using var factory = new DeleteFailingWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        var campaignId = await CreateDraftAsync(client);
        var assetId = await UploadAssetAsync(client, campaignId, "pending-delete.png", new byte[] { 1, 2, 3 });

        using var delete = await client.DeleteAsync(AssetRoute(campaignId, assetId));
        using var access = await client.GetAsync($"/api/files/{assetId}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, access.StatusCode);
        var asset = await WalletCampaignWorkflowTestHelpers.FindAssetAsync(factory, assetId);
        Assert.NotNull(asset);
        Assert.Equal(StorageObjectState.DeletionPending, asset.StorageState);
        Assert.Null(asset.DeletedAtUtc);
    }

    [Fact]
    public async Task ApprovedAsset_CannotBeReplacedOrDeleted()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        var campaignId = await CreateDraftAsync(client);
        var assetId = await UploadAssetAsync(client, campaignId, "approved.png", new byte[] { 1, 2, 3 });
        await MarkReviewStatusAsync(factory, assetId, StoredFileReviewStatus.Approved);

        using var replacement = await UploadReplacementAsync(client, campaignId, assetId, "new.png", new byte[] { 4, 5, 6 });
        using var delete = await client.DeleteAsync(AssetRoute(campaignId, assetId));

        Assert.Equal(HttpStatusCode.Conflict, replacement.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var asset = await WalletCampaignWorkflowTestHelpers.FindAssetAsync(factory, assetId);
        Assert.NotNull(asset);
        Assert.Equal(StorageObjectState.Active, asset.StorageState);
        Assert.Null(asset.SupersededByFileId);
        Assert.Null(asset.DeletedAtUtc);
    }

    private static async Task<string> CreateDraftAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Lifecycle draft", Description = "Draft for file lifecycle testing." });
        response.EnsureSuccessStatusCode();
        return await ReadDataStringAsync(response, "campaignId");
    }

    private static async Task<string> UploadAssetAsync(HttpClient client, string campaignId, string fileName, byte[] content)
    {
        using var response = await PostFileAsync(client, $"/api/company/campaigns/{campaignId}/assets", fileName, content);
        response.EnsureSuccessStatusCode();
        return await ReadAssetIdAsync(response);
    }

    private static Task<HttpResponseMessage> UploadReplacementAsync(
        HttpClient client,
        string campaignId,
        string assetId,
        string fileName,
        byte[] content)
        => PostFileAsync(client, $"/api/company/campaigns/{campaignId}/assets/{assetId}/replacement", fileName, content);

    private static Task<HttpResponseMessage> PostFileAsync(HttpClient client, string route, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        return client.PostAsync(route, form);
    }

    private static string AssetRoute(string campaignId, string assetId)
        => $"/api/company/campaigns/{campaignId}/assets/{assetId}";

    private static async Task MarkReviewStatusAsync(
        ConfiguredWebAppFactory factory,
        string assetId,
        StoredFileReviewStatus reviewStatus)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridge.Repository.Data.MediBridgeDbContext>();
        var asset = await context.StoredFiles.FindAsync(assetId);
        Assert.NotNull(asset);
        asset.ReviewStatus = reviewStatus;
        await context.SaveChangesAsync();
    }

    private static Task<string> ReadAssetIdAsync(HttpResponseMessage response)
        => ReadDataStringAsync(response, "assetId");

    private static async Task<string> ReadDataStringAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("Data").GetProperty(propertyName).GetString()!;
    }

    private sealed class DeleteFailingWebAppFactory : ConfiguredWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton<IFileStorageProvider, DeleteFailingStorageProvider>();
            });
        }
    }

    private sealed class DeleteFailingStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResult> UploadAsync(
            FileStorageUpload request,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            var storageKey = $"failing-delete/{Guid.NewGuid():N}/{Path.GetFileName(request.OriginalFileName)}";
            return Task.FromResult(new FileStorageUploadResult(storageKey, "raw"));
        }

        public Task<SignedFileUrl> CreateSignedReadUrlAsync(
            string storageKey,
            string resourceType,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default)
        {
            var url = new Uri($"https://files.test/{Uri.EscapeDataString(storageKey)}");
            return Task.FromResult(new SignedFileUrl(url, DateTime.UtcNow.Add(lifetime)));
        }

        public Task DeleteAsync(
            string storageKey,
            string resourceType,
            CancellationToken cancellationToken = default)
            => throw new FileStorageUnavailableException("File storage provider is unavailable.");
    }
}
