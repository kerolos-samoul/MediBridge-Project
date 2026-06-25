using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests.Files;

public sealed class FileStorageContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task FileAccess_Anonymous_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, ids.CompanyProfileId, ids.DoctorProfileId);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(FileRoute(campaign.AssetId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Fact]
    public async Task FileAccess_WrongCompany_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await CreateApprovedActorsAsync(factory);
        var other = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, owner.CompanyProfileId, owner.DoctorProfileId);
        using var client = CreateCompanyClient(factory, other.CompanyUserId);

        using var response = await client.GetAsync(FileRoute(campaign.AssetId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task FileAccess_MissingOrDeletedFile_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, ids.CompanyProfileId, ids.DoctorProfileId);
        await MarkAssetDeletedAsync(factory, campaign.AssetId);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var missing = await client.GetAsync(FileRoute(Guid.NewGuid().ToString("N")));
        using var deleted = await client.GetAsync(FileRoute(campaign.AssetId));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await AssertEnvelopeAsync(missing, 404);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        await AssertEnvelopeAsync(deleted, 404);
    }

    [Fact]
    public async Task FileAccess_OwnerAndAdmin_ReturnSignedHttpsUrlWithoutProviderSecrets()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        var campaign = await CreateCampaignForReviewAsync(factory, ids.CompanyProfileId, ids.DoctorProfileId);
        using var company = CreateCompanyClient(factory, ids.CompanyUserId);
        using var admin = CreateAdminClient(factory, ids.AdminUserId);

        using var ownerResponse = await company.GetAsync(FileRoute(campaign.AssetId));
        using var adminResponse = await admin.GetAsync(FileRoute(campaign.AssetId));

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        AssertSignedAccessEnvelope(await AssertEnvelopeAsync(ownerResponse, 200, "Success"), campaign.AssetId);
        AssertSignedAccessEnvelope(await AssertEnvelopeAsync(adminResponse, 200, "Success"), campaign.AssetId);
    }

    private static string FileRoute(string fileId) => $"/api/files/{fileId}";

    private static async Task MarkAssetDeletedAsync(ContractWebAppFactory factory, string assetId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var asset = await context.StoredFiles.SingleAsync(file => file.Id == assetId);
        asset.StorageState = StorageObjectState.Deleted;
        asset.DeletedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    private static void AssertSignedAccessEnvelope(JsonElement envelope, string fileId)
    {
        var data = envelope.GetProperty("Data");
        Assert.Equal(fileId, data.GetProperty("FileId").GetString());
        var url = data.GetProperty("Url").GetString();
        Assert.NotNull(url);
        Assert.StartsWith("https://", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cloudinary://", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_secret", url, StringComparison.OrdinalIgnoreCase);

        var expiresAtUtc = data.GetProperty("ExpiresAtUtc").GetDateTime();
        Assert.InRange(expiresAtUtc, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(6));
    }
}
