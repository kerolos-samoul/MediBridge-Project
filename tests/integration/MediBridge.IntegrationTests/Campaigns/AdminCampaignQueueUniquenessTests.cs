using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignQueueUniquenessTests
{
    [Fact]
    public async Task ActiveQueueRows_EnforceOneRowPerCampaignAndDoctorAtDatabaseBoundary()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = new Campaign
        {
            CompanyId = actors.CompanyProfileId,
            Title = "Queue uniqueness",
            Description = "Database invariant coverage.",
            Status = CampaignStatus.Approved
        };
        await context.Campaigns.AddAsync(campaign);
        await context.SaveChangesAsync();
        await context.DoctorMessageQueues.AddRangeAsync(
            CreateQueue(campaign.Id, actors.DoctorProfileId, QueueItemStatus.Queued),
            CreateQueue(campaign.Id, actors.DoctorProfileId, QueueItemStatus.Activated));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task CancelledQueueRow_DoesNotPreventAReplacementActiveRow()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = new Campaign
        {
            CompanyId = actors.CompanyProfileId,
            Title = "Queue replacement",
            Description = "Cancelled rows are historical.",
            Status = CampaignStatus.Approved
        };
        await context.Campaigns.AddAsync(campaign);
        await context.SaveChangesAsync();
        await context.DoctorMessageQueues.AddRangeAsync(
            CreateQueue(campaign.Id, actors.DoctorProfileId, QueueItemStatus.Cancelled),
            CreateQueue(campaign.Id, actors.DoctorProfileId, QueueItemStatus.Queued));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaign.Id));
    }

    private static DoctorMessageQueue CreateQueue(string campaignId, string doctorId, QueueItemStatus status)
        => new()
        {
            CampaignId = campaignId,
            DoctorId = doctorId,
            QueuedAtUtc = DateTime.UtcNow,
            CampaignSubmittedAtUtc = DateTime.UtcNow,
            Status = status
        };
}
