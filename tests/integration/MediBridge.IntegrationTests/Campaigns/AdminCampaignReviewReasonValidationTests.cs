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

public sealed class AdminCampaignReviewReasonValidationTests
{
    [Theory]
    [InlineData("Rejected")]
    [InlineData("RevisionRequired")]
    public async Task NonApprovalDecision_WithoutPublicReason_ReturnsValidationEnvelopeWithoutMutation(string decision)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaign.CampaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = decision, Reason = "   " })
        };
        request.Headers.Add("Idempotency-Key", $"reason-required-{decision}");

        using var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(
            CampaignStatus.PendingReview,
            (await context.Campaigns.AsNoTracking().SingleAsync(item => item.Id == campaign.CampaignId)).Status);
        Assert.Equal(0, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaign.CampaignId));
    }
}
