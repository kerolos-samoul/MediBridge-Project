using System.Net;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Interfaces.Files;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.ContractTests.Admin;

public sealed class AdminCampaignReviewDetailContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task AdminCampaignReviewDetail_ReturnsSignedReviewPackageEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreatePendingReviewCampaignFixtureAsync(
            factory,
            actors.CompanyProfileId,
            new DateTime(2026, 6, 26, 9, 0, 0, DateTimeKind.Utc));
        await AddTargetSnapshotFixtureAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        var fileId = await AddApprovedCampaignMediaFixtureAsync(factory, campaign.CampaignId);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await client.GetAsync(Route(campaign.CampaignId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(campaign.CampaignId, data.GetProperty("campaignId").GetString());
        Assert.Equal("PendingReview", data.GetProperty("status").GetString());
        var file = Assert.Single(data.GetProperty("reviewableMediaAssets").EnumerateArray());
        Assert.Equal(fileId, file.GetProperty("fileId").GetString());
        Assert.StartsWith("https://", file.GetProperty("accessUrl").GetString(), StringComparison.Ordinal);
        Assert.True(file.TryGetProperty("accessExpiresAtUtc", out var expiry));
        Assert.True(expiry.GetDateTime() > DateTime.UtcNow);
    }

    [Fact]
    public async Task AdminCampaignReviewDetail_UnknownCampaign_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = CreateAdminClient(factory, actors.AdminUserId);

        using var response = await client.GetAsync(Route(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    [Fact]
    public async Task AdminCampaignReviewDetail_WithoutAuthentication_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Fact]
    public async Task AdminCampaignReviewDetail_ForNonAdmin_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        using var client = CreateDoctorClient(factory, actors.DoctorUserId);

        using var response = await client.GetAsync(Route(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task AdminCampaignReviewDetail_WhenStorageIsUnavailable_ReturnsSafeServiceUnavailableEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var campaign = await CreatePendingReviewCampaignFixtureAsync(
            factory,
            actors.CompanyProfileId,
            new DateTime(2026, 6, 26, 9, 30, 0, DateTimeKind.Utc));
        await AddApprovedCampaignMediaFixtureAsync(factory, campaign.CampaignId);
        using var authenticatedClient = CreateAdminClient(factory, actors.AdminUserId);
        await using var unavailableFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton<IFileStorageProvider, UnavailableFileStorageProvider>();
            }));
        using var client = unavailableFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticatedClient.DefaultRequestHeaders.Authorization;

        using var response = await client.GetAsync(Route(campaign.CampaignId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertEnvelopeAsync(response, 503, "File storage is temporarily unavailable.");
    }

    private static string Route(string campaignId)
        => WalletCampaignWorkflowRoutes.AdminCampaignReviewDetail.Replace("{campaignId}", campaignId, StringComparison.Ordinal);

    private sealed class UnavailableFileStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResult> UploadAsync(
            FileStorageUpload request,
            Stream content,
            CancellationToken cancellationToken = default)
            => throw new FileStorageUnavailableException("File storage provider is unavailable.");

        public Task<SignedFileUrl> CreateSignedReadUrlAsync(
            string storageKey,
            string resourceType,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default)
            => throw new FileStorageUnavailableException("File storage provider is unavailable.");

        public Task DeleteAsync(
            string storageKey,
            string resourceType,
            CancellationToken cancellationToken = default)
            => throw new FileStorageUnavailableException("File storage provider is unavailable.");
    }
}
