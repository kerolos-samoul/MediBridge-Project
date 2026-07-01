using System.Net;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignQueueOrderingWorkflowTests
{
    [Fact]
    public async Task AdminQueueRows_AreOrderedBySubmissionTimeThenQueuedTimeThenQueueId()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var secondActors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var thirdActors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaignId = await SeedOrderedRowsAsync(
            factory,
            actors.CompanyProfileId,
            [actors.DoctorProfileId, secondActors.DoctorProfileId, thirdActors.DoctorProfileId]);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var response = await admin.GetAsync($"/api/admin/campaigns/{campaignId}/queue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("Data").EnumerateArray().ToArray();
        Assert.Equal(new[] { "queue-c", "queue-a", "queue-b" }, rows.Select(row => row.GetProperty("queueId").GetString()).ToArray());
        Assert.All(rows, row => Assert.Equal("Queued", row.GetProperty("status").GetString()));
        Assert.All(rows, row => Assert.True(row.TryGetProperty("campaignSubmittedAtUtc", out _)));
        Assert.All(rows, row => Assert.True(row.TryGetProperty("queuedAtUtc", out _)));
    }

    private static async Task<string> SeedOrderedRowsAsync(
        WebAppFactory factory,
        string companyId,
        IReadOnlyList<string> doctorIds)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Queue order",
            Description = "Queue rows must remain FIFO.",
            Status = CampaignStatus.Approved
        };
        var firstTime = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
        await context.Campaigns.AddAsync(campaign);
        await context.DoctorMessageQueues.AddRangeAsync(
            new DoctorMessageQueue { Id = "queue-c", CampaignId = campaign.Id, DoctorId = doctorIds[2], QueuedAtUtc = firstTime.AddMinutes(5), CampaignSubmittedAtUtc = firstTime.AddMinutes(-1), Status = QueueItemStatus.Queued },
            new DoctorMessageQueue { Id = "queue-b", CampaignId = campaign.Id, DoctorId = doctorIds[1], QueuedAtUtc = firstTime, CampaignSubmittedAtUtc = firstTime, Status = QueueItemStatus.Queued },
            new DoctorMessageQueue { Id = "queue-a", CampaignId = campaign.Id, DoctorId = doctorIds[0], QueuedAtUtc = firstTime, CampaignSubmittedAtUtc = firstTime, Status = QueueItemStatus.Queued });
        await context.SaveChangesAsync();
        return campaign.Id;
    }
}
