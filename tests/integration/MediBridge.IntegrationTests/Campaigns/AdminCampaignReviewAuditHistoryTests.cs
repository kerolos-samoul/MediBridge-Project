using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignReviewAuditHistoryTests
{
    [Fact]
    public async Task RevisionResubmissionAndSecondReview_PreserveIndependentAppendOnlyHistory()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var secondReviewer = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var firstAdmin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var secondAdmin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, secondReviewer.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        await SetDoctorPriceAsync(firstAdmin, actors.DoctorProfileId);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddHours(-1),
            createWalletRow: true,
            availableBalance: 500m);
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(factory, campaign.CampaignId, actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddPendingCampaignMediaAsync(factory, campaign.CampaignId);

        using var firstReview = await ReviewAsync(
            firstAdmin,
            campaign.CampaignId,
            "audit-history-first-review",
            "RevisionRequired",
            "Correct the public wording.",
            "First reviewer note.");
        Assert.Equal(HttpStatusCode.OK, firstReview.StatusCode);

        using var update = await company.PutAsJsonAsync(
            $"/api/company/campaigns/{campaign.CampaignId}",
            new
            {
                Title = "Revised campaign title",
                Description = "Revised campaign description.",
                ClinicalResearchInfo = "Revised clinical context."
            });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var resubmit = await SubmitAsync(company, campaign.CampaignId, "audit-history-resubmission");
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);

        using var secondReview = await ReviewAsync(
            secondAdmin,
            campaign.CampaignId,
            "audit-history-second-review",
            "Rejected",
            "Final compliance rejection.",
            "Second reviewer note.");
        Assert.Equal(HttpStatusCode.OK, secondReview.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var history = await context.CampaignReviewHistories
            .AsNoTracking()
            .Where(item => item.CampaignId == campaign.CampaignId)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .ToArrayAsync();

        Assert.Equal(2, history.Length);
        Assert.Equal(actors.AdminUserId, history[0].AdminUserId);
        Assert.Equal(CampaignReviewDecision.RevisionRequired, history[0].Decision);
        Assert.Equal("Correct the public wording.", history[0].Reason);
        Assert.Equal("First reviewer note.", history[0].Notes);
        Assert.Equal(CampaignStatus.PendingReview, history[0].PriorStatus);
        Assert.Equal(CampaignStatus.RevisionRequired, history[0].ResultingStatus);
        Assert.Equal("audit-history-first-review", history[0].IdempotencyKey);
        Assert.Equal(secondReviewer.AdminUserId, history[1].AdminUserId);
        Assert.Equal(CampaignReviewDecision.Rejected, history[1].Decision);
        Assert.Equal("Final compliance rejection.", history[1].Reason);
        Assert.Equal("Second reviewer note.", history[1].Notes);
        Assert.Equal(CampaignStatus.PendingReview, history[1].PriorStatus);
        Assert.Equal(CampaignStatus.Rejected, history[1].ResultingStatus);
        Assert.Equal("audit-history-second-review", history[1].IdempotencyKey);
        Assert.NotEqual(history[0].Id, history[1].Id);
        Assert.True(history[1].CreatedAtUtc > history[0].CreatedAtUtc);
    }

    [Fact]
    public async Task StaleCancelledAndDeletedCampaigns_ReturnExpectedEnvelopeWithoutAppendingHistory()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        var reviewed = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-10));
        var cancelled = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-9));
        var deleted = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-8));

        using var completedReview = await ReviewAsync(
            admin,
            reviewed.CampaignId,
            "audit-stale-original",
            "Rejected",
            "Final rejection.",
            null);
        Assert.Equal(HttpStatusCode.OK, completedReview.StatusCode);
        await SetCampaignStateAsync(factory, cancelled.CampaignId, CampaignStatus.Cancelled, isDeleted: false);
        await SetCampaignStateAsync(factory, deleted.CampaignId, CampaignStatus.PendingReview, isDeleted: true);

        using var staleResponse = await ReviewAsync(admin, reviewed.CampaignId, "audit-stale-new-key", "Approved", null, null);
        using var cancelledResponse = await ReviewAsync(admin, cancelled.CampaignId, "audit-cancelled-key", "Approved", null, null);
        using var deletedResponse = await ReviewAsync(admin, deleted.CampaignId, "audit-deleted-key", "Approved", null, null);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelledResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        await AssertEnvelopeCodeAsync(staleResponse, 409);
        await AssertEnvelopeCodeAsync(cancelledResponse, 409);
        await AssertEnvelopeCodeAsync(deletedResponse, 404);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == reviewed.CampaignId));
        Assert.Equal(0, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == cancelled.CampaignId));
        Assert.Equal(0, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == deleted.CampaignId));
    }

    private static async Task SetDoctorPriceAsync(HttpClient admin, string doctorId)
    {
        using var response = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{doctorId}/price",
            new { PricePerMessage = 50m, Reason = "Audit history workflow price" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SetCampaignStateAsync(
        WebAppFactory factory,
        string campaignId,
        CampaignStatus status,
        bool isDeleted)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.SingleAsync(item => item.Id == campaignId);
        campaign.Status = status;
        campaign.IsDeleted = isDeleted;
        campaign.DeletedAtUtc = isDeleted ? DateTime.UtcNow : null;
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

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string campaignId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{campaignId}/submit");
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task AssertEnvelopeCodeAsync(HttpResponseMessage response, int expectedCode)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }
}
