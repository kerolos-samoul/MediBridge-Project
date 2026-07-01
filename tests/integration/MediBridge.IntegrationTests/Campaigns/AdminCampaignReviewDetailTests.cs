using System.Net;
using System.Text.Json;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignReviewDetailTests
{
    [Fact]
    public async Task AdminCampaignReviewDetail_ReturnsOnlyActiveReviewFilesWithShortLivedSignedAccess()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            new DateTime(2026, 6, 26, 11, 0, 0, DateTimeKind.Utc));
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        var pendingId = await WalletCampaignWorkflowTestHelpers.AddPendingCampaignMediaAsync(factory, campaign.CampaignId);
        var approvedId = await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        var fixtures = await AddOptionalAndInactiveFilesAsync(factory, campaign.CampaignId);
        using var client = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        var requestedAtUtc = DateTime.UtcNow;

        using var response = await client.GetAsync($"/api/admin/campaigns/{campaign.CampaignId}/review-detail");

        var completedAtUtc = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("Data");
        var reviewable = data.GetProperty("reviewableMediaAssets").EnumerateArray().ToArray();
        var optional = data.GetProperty("optionalFiles").EnumerateArray().ToArray();
        Assert.Equal(new[] { pendingId, approvedId }.OrderBy(id => id), reviewable.Select(FileId).OrderBy(id => id));
        Assert.Equal(fixtures.OptionalIds.OrderBy(id => id), optional.Select(FileId).OrderBy(id => id));

        foreach (var file in reviewable.Concat(optional))
        {
            var accessUrl = file.GetProperty("accessUrl").GetString();
            Assert.True(Uri.TryCreate(accessUrl, UriKind.Absolute, out var uri));
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            var expiresAtUtc = file.GetProperty("accessExpiresAtUtc").GetDateTime();
            Assert.True(expiresAtUtc > requestedAtUtc);
            Assert.True(expiresAtUtc <= completedAtUtc.AddMinutes(5).AddSeconds(5));
        }

        Assert.DoesNotContain(fixtures.DeletedId, json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixtures.SupersededId, json, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CloudinaryUrl", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerCredential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internalAdminNote", json, StringComparison.OrdinalIgnoreCase);
        foreach (var storageKey in fixtures.StorageKeys)
        {
            Assert.DoesNotContain(storageKey, json, StringComparison.Ordinal);
        }
    }

    private static string FileId(JsonElement file) => file.GetProperty("fileId").GetString()!;

    private static async Task<ReviewFileFixtureIds> AddOptionalAndInactiveFilesAsync(WebAppFactory factory, string campaignId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var voice = CreateFile(campaignId, StoredFilePurpose.VoiceNote, StoredFileReviewStatus.Pending, "review/voice-note.mp3");
        var clinical = CreateFile(campaignId, StoredFilePurpose.ClinicalResearchAttachment, StoredFileReviewStatus.Rejected, "review/clinical.pdf");
        var deleted = CreateFile(campaignId, StoredFilePurpose.CampaignMedia, StoredFileReviewStatus.Pending, "review/deleted.png");
        deleted.StorageState = StorageObjectState.Deleted;
        deleted.DeletedAtUtc = DateTime.UtcNow;
        var superseded = CreateFile(campaignId, StoredFilePurpose.CampaignMedia, StoredFileReviewStatus.Approved, "review/superseded.png");
        superseded.SupersededByFileId = Guid.NewGuid().ToString("N");
        await context.StoredFiles.AddRangeAsync(voice, clinical, deleted, superseded);
        await context.SaveChangesAsync();
        return new ReviewFileFixtureIds(
            new[] { voice.Id, clinical.Id },
            deleted.Id,
            superseded.Id,
            new[] { voice.StorageKey, clinical.StorageKey, deleted.StorageKey, superseded.StorageKey });
    }

    private static StoredFile CreateFile(
        string campaignId,
        StoredFilePurpose purpose,
        StoredFileReviewStatus reviewStatus,
        string storageKey)
        => new()
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaignId,
            Purpose = purpose,
            OriginalFileName = Path.GetFileName(storageKey),
            ContentType = purpose == StoredFilePurpose.VoiceNote ? "audio/mpeg" : "application/pdf",
            SizeBytes = 2048,
            StorageKey = storageKey,
            StorageState = StorageObjectState.Active,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = reviewStatus,
            CreatedAtUtc = DateTime.UtcNow
        };

    private sealed record ReviewFileFixtureIds(
        IReadOnlyList<string> OptionalIds,
        string DeletedId,
        string SupersededId,
        IReadOnlyList<string> StorageKeys);
}
