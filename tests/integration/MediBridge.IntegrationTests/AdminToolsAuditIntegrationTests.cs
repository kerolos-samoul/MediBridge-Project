using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminToolsAuditIntegrationTests
{
    [Fact]
    public async Task AccountFileAndCampaignModeration_AuditEvidenceIncludesSafeDecisionContext()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await AdminToolsWorkQueueIntegrationTests.SeedWorkQueueSourcesAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin));

        string accountId;
        string fileId;
        string campaignId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            accountId = await db.Users
                .Where(user => user.AccountStatus == AccountStatus.Pending && user.Role == UserRole.Doctor)
                .Select(user => user.Id)
                .SingleAsync();
            fileId = await db.StoredFiles
                .Where(file => file.ReviewStatus == StoredFileReviewStatus.Pending)
                .Select(file => file.Id)
                .SingleAsync();
            campaignId = await db.Campaigns
                .Where(campaign => campaign.Status == CampaignStatus.PendingReview)
                .Select(campaign => campaign.Id)
                .SingleAsync();
        }

        using var accountResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{accountId}/decision", new
        {
            Decision = "Reject",
            Reason = "License document is unreadable.",
            Notes = "Internal account note."
        });
        using var fileResponse = await client.PutAsJsonAsync($"/api/admin/files/{fileId}/review", new
        {
            Decision = "Rejected",
            Reason = "File is not readable.",
            Notes = "Internal file note."
        });
        using var campaignRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new
            {
                decision = "ChangesRequested",
                reason = "Please revise the campaign.",
                notes = "Internal campaign note."
            })
        };
        campaignRequest.Headers.Add("Idempotency-Key", "audit-review-001");
        using var campaignResponse = await client.SendAsync(campaignRequest);

        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, campaignResponse.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var accountAudit = await context.AuthenticationAuditEvents.SingleAsync(audit =>
            audit.EventType == AuthAuditEventType.AdminDecision && audit.TargetUserId == accountId);
        Assert.Equal(admin, accountAudit.ActorUserId);
        Assert.Equal("License document is unreadable.", accountAudit.Reason);
        Assert.Contains("Pending->Rejected", accountAudit.Outcome, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(accountAudit.CorrelationId));
        Assert.True(accountAudit.CreatedAtUtc > DateTime.MinValue);

        var fileAudit = await context.AuditEvents.SingleAsync(audit =>
            audit.EventType == "FileReviewDecisionRecorded" && audit.TargetId == fileId);
        Assert.Equal(fileId, fileAudit.TargetId);
        Assert.Contains(admin, fileAudit.Metadata, StringComparison.Ordinal);
        Assert.Contains("Pending", fileAudit.Metadata, StringComparison.Ordinal);
        Assert.Contains("Rejected", fileAudit.Metadata, StringComparison.Ordinal);
        Assert.Contains("File is not readable.", fileAudit.Metadata, StringComparison.Ordinal);
        Assert.Contains("ReviewedAtUtc", fileAudit.Metadata, StringComparison.Ordinal);
        Assert.True(fileAudit.CreatedAtUtc > DateTime.MinValue);

        var campaignAudit = await context.AuditEvents.SingleAsync(audit =>
            audit.EventType == "CampaignReviewCompleted" && audit.TargetId == campaignId);
        Assert.Equal(admin, campaignAudit.ActorUserId);
        Assert.Equal("Please revise the campaign.", campaignAudit.Reason);
        Assert.Contains("PendingReview", campaignAudit.Metadata, StringComparison.Ordinal);
        Assert.Contains("RevisionRequired", campaignAudit.Metadata, StringComparison.Ordinal);
        Assert.Contains("ReviewedAtUtc", campaignAudit.Metadata, StringComparison.Ordinal);
        Assert.True(campaignAudit.CreatedAtUtc > DateTime.MinValue);
    }
}
