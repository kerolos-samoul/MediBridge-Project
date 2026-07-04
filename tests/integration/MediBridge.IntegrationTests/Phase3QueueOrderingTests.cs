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
        var oldSubmissionTime = new DateTime(2026, 5, 31, 8, 0, 0, DateTimeKind.Utc);
        var tiedSubmissionTime = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var laterSubmissionTime = new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc);
        var olderCampaignId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId, submittedAtUtc: oldSubmissionTime);
        var sameTimeCampaignBId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId, submittedAtUtc: tiedSubmissionTime);
        var sameTimeCampaignAId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId, submittedAtUtc: tiedSubmissionTime);
        var laterCampaignId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId, submittedAtUtc: laterSubmissionTime);
        var deliberatelyReversedQueueTime = new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc);

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-carry-over", ids.DoctorProfileId, olderCampaignId, oldSubmissionTime, deliberatelyReversedQueueTime.AddMinutes(3));
        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-tie-b", ids.DoctorProfileId, sameTimeCampaignBId, tiedSubmissionTime, deliberatelyReversedQueueTime.AddMinutes(2));
        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-tie-a", ids.DoctorProfileId, sameTimeCampaignAId, tiedSubmissionTime, deliberatelyReversedQueueTime.AddMinutes(1));
        await unitOfWork.MessageQueues.AddQueueItemAsync("queue-next-day", ids.DoctorProfileId, laterCampaignId, laterSubmissionTime, deliberatelyReversedQueueTime);
        await unitOfWork.SaveChangesAsync();

        var orderedIds = await unitOfWork.MessageQueues.ListActiveQueueItemIdsForDoctorAsync(ids.DoctorProfileId, QueueItemStatus.Queued);

        Assert.Equal(new[] { "queue-carry-over", "queue-tie-a", "queue-tie-b", "queue-next-day" }, orderedIds);
    }
}
