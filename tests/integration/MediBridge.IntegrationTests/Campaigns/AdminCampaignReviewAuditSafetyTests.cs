using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignReviewAuditSafetyTests
{
    [Fact]
    public async Task CompletedReplayAndConflictAudits_UseSafeMetadataOnly()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        var storageKey = $"provider/private/{campaign.CampaignId}/asset.png";
        await AddApprovedMediaAsync(factory, campaign.CampaignId, storageKey);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        const string jwtMarker = "header.payload.signature";
        const string rawBodyMarker = "raw-request-body-marker";
        const string credentialMarker = "provider-credential-marker";
        const string stackMarker = "internal-stack-trace-marker";
        var notes = $"{jwtMarker} {rawBodyMarker} {credentialMarker} {storageKey} {stackMarker}";

        using var completed = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "audit-safety-key",
            "RevisionRequired",
            "Public correction required.",
            notes);
        using var replay = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "audit-safety-key",
            "RevisionRequired",
            "Public correction required.",
            notes);
        using var conflict = await ReviewAsync(
            admin,
            campaign.CampaignId,
            "audit-safety-key",
            "RevisionRequired",
            "Public correction required.",
            "Different internal note.");

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var auditEvents = await context.AuditEvents
            .AsNoTracking()
            .Where(item => item.TargetType == AuditTargetType.Campaign && item.TargetId == campaign.CampaignId)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .ToArrayAsync();

        Assert.Collection(
            auditEvents,
            item => Assert.Equal("CampaignReviewed", item.EventType),
            item => Assert.Equal("CampaignReviewReplayed", item.EventType),
            item => Assert.Equal("CampaignReviewConflict", item.EventType));
        Assert.All(auditEvents, item =>
        {
            var serialized = JsonSerializer.Serialize(item);
            Assert.DoesNotContain(jwtMarker, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(rawBodyMarker, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(credentialMarker, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(storageKey, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(stackMarker, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("RequestBody", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StorageKey", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StackTrace", serialized, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static async Task AddApprovedMediaAsync(WebAppFactory factory, string campaignId, string storageKey)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.StoredFiles.AddAsync(new MediBridge.Core.Entities.Files.StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaignId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "audit-safety.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = storageKey,
            StorageState = StorageObjectState.Active,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> ReviewAsync(
        HttpClient client,
        string campaignId,
        string key,
        string decision,
        string? reason,
        string? notes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = decision, Reason = reason, Notes = notes })
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
}
