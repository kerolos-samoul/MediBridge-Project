using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3QueueOrderingTests
{
    [Fact]
    public async Task ListActiveQueueItemIdsForDoctorAsync_ReturnsFifoOrderWithIdTieBreakerAndCarryOver()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var olderCampaignId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId);
        var sameTimeCampaignAId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId);
        var sameTimeCampaignBId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId);
        var laterCampaignId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId);
        var oldCarryOverTime = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var sameQueuedTime = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var laterNextDayTime = new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc);

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-carry-over", ids.DoctorProfileId, olderCampaignId, oldCarryOverTime);
        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-tie-b", ids.DoctorProfileId, sameTimeCampaignBId, sameQueuedTime);
        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-tie-a", ids.DoctorProfileId, sameTimeCampaignAId, sameQueuedTime);
        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-next-day", ids.DoctorProfileId, laterCampaignId, laterNextDayTime);
        await unitOfWork.SaveChangesAsync();

        var orderedIds = await unitOfWork.MessageQueues.ListActiveQueueItemIdsForDoctorAsync(ids.DoctorProfileId, QueueItemStatus.Queued);

        Assert.Equal(new[] { "queue-carry-over", "queue-tie-a", "queue-tie-b", "queue-next-day" }, orderedIds);
    }
}
