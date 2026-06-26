using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase5QueueCreationIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase5QueueCreationIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ApprovedCampaignQueueCreation_CreatesOneQueuedRowPerEligibleTarget()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var firstDoctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 50m, activityScore: 90m);
        var secondDoctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 75m, activityScore: 91m);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved);
        await AddCampaignTargetAsync(campaignId, firstDoctor.DoctorId);
        await AddCampaignTargetAsync(campaignId, secondDoctor.DoctorId);
        var queuedAtUtc = new DateTime(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);

        var result = await Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, queuedAtUtc, actorUserId: company.UserId);

        Assert.Equal(campaignId, result.CampaignId);
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.DuplicateExistingCount);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var queueItems = await context.DoctorMessageQueues.Where(queue => queue.CampaignId == campaignId).ToListAsync();
        Assert.Equal(2, queueItems.Count);
        Assert.All(queueItems, queue =>
        {
            Assert.Equal(QueueItemStatus.Queued, queue.Status);
            Assert.Equal(queuedAtUtc, queue.QueuedAtUtc);
            Assert.NotNull(queue.CampaignSubmittedAtUtc);
        });
    }

    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.PendingReview)]
    [InlineData(CampaignStatus.Rejected)]
    [InlineData(CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Completed)]
    [InlineData(CampaignStatus.Cancelled)]
    public async Task QueueCreation_CreatesNoRowsForNonApprovedCampaignStatuses(CampaignStatus status)
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, status);
        await AddCampaignTargetAsync(campaignId, doctor.DoctorId);

        var result = await Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, DateTime.UtcNow, actorUserId: company.UserId);

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.DuplicateExistingCount);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == campaignId));
    }

    [Fact]
    public async Task QueueCreation_CreatesNoRowsForSoftDeletedApprovedCampaign()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved);
        await AddCampaignTargetAsync(campaignId, doctor.DoctorId);
        await UpdateCampaignAsync(campaignId, campaign =>
        {
            campaign.IsDeleted = true;
            campaign.DeletedAtUtc = DateTime.UtcNow;
        });

        var result = await Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, DateTime.UtcNow, actorUserId: company.UserId);

        Assert.Equal(0, result.CreatedCount);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == campaignId));
    }

    [Fact]
    public async Task QueueCreation_RetryAfterPartialCompletionCreatesMissingRowsOnly()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var firstDoctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var secondDoctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved);
        await AddCampaignTargetAsync(campaignId, firstDoctor.DoctorId);
        await AddCampaignTargetAsync(campaignId, secondDoctor.DoctorId);
        await Phase5CampaignQueueTestHelpers.SeedQueueItemAsync(factory.Services, campaignId, firstDoctor.DoctorId);

        var firstRetry = await Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, DateTime.UtcNow, actorUserId: company.UserId);
        var secondRetry = await Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, DateTime.UtcNow, actorUserId: company.UserId);

        Assert.Equal(1, firstRetry.CreatedCount);
        Assert.Equal(1, firstRetry.DuplicateExistingCount);
        Assert.Equal(0, secondRetry.CreatedCount);
        Assert.Equal(2, secondRetry.DuplicateExistingCount);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(2, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == campaignId));
        Assert.Equal(2, await context.DoctorMessageQueues.Select(queue => new { queue.CampaignId, queue.DoctorId }).Distinct().CountAsync(pair => pair.CampaignId == campaignId));
    }

    [Fact]
    public async Task QueueCreation_ConcurrentRetriesAreIdempotentAndDoNotSurfaceDuplicateKeyFailures()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved);
        for (var index = 0; index < 12; index++)
        {
            var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 50m + index, activityScore: 90m - index);
            await AddCampaignTargetAsync(campaignId, doctor.DoctorId);
        }

        var queuedAtUtc = new DateTime(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, queuedAtUtc, actorUserId: company.UserId))
            .ToArray();

        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(attempts));

        Assert.Null(exception);
        var results = attempts.Select(attempt => attempt.Result).ToArray();
        Assert.Equal(12, results.Sum(result => result.CreatedCount));
        Assert.All(results, result => Assert.Equal(12, result.CreatedCount + result.DuplicateExistingCount));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(12, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == campaignId));
        Assert.Equal(12, await context.DoctorMessageQueues.Select(queue => new { queue.CampaignId, queue.DoctorId }).Distinct().CountAsync(pair => pair.CampaignId == campaignId));
    }

    [Fact]
    public async Task QueueCreation_SkipsTargetsThatBecomeIneligibleBeforeApprovalAndAuditsThem()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var eligible = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var suspended = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var softDeleted = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var unapproved = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var zeroPrice = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved);
        foreach (var doctorId in new[] { eligible.DoctorId, suspended.DoctorId, softDeleted.DoctorId, unapproved.DoctorId, zeroPrice.DoctorId })
        {
            await AddCampaignTargetAsync(campaignId, doctorId);
        }

        await UpdateDoctorAsync(suspended.DoctorId, doctor => doctor.Status = DoctorMarketplaceStatus.Suspended);
        await UpdateDoctorAsync(softDeleted.DoctorId, doctor =>
        {
            doctor.IsDeleted = true;
            doctor.DeletedAtUtc = DateTime.UtcNow;
        });
        await UpdateDoctorUserStatusAsync(unapproved.UserId, AccountStatus.Pending);
        await UpdateDoctorAsync(zeroPrice.DoctorId, doctor => doctor.PricePerMessage = 0m);

        var result = await Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, DateTime.UtcNow, actorUserId: company.UserId);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(4, result.SkippedCount);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await context.DoctorMessageQueues.CountAsync(queue => queue.CampaignId == campaignId));
        Assert.Equal(4, await context.AuditEvents.CountAsync(audit => audit.EventType == "Phase5QueueCreationSkippedTarget" && audit.TargetId == campaignId));
    }

    [Fact]
    public async Task PendingQueueReads_OrderByQueuedAtThenStableId()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var queuedAtUtc = new DateTime(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);
        await AddQueueItemWithIdAsync("queue-z-old", await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved), doctor.DoctorId, queuedAtUtc.AddMinutes(-1));
        await AddQueueItemWithIdAsync("queue-b-same", await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved), doctor.DoctorId, queuedAtUtc);
        await AddQueueItemWithIdAsync("queue-a-same", await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved), doctor.DoctorId, queuedAtUtc);
        await AddQueueItemWithIdAsync("queue-new", await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved), doctor.DoctorId, queuedAtUtc.AddMinutes(1));

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MediBridge.Core.Interfaces.IDomainUnitOfWork>();
        var queueItems = await unitOfWork.MessageQueues.ListQueueItemsForDoctorAsync(doctor.DoctorId, QueueItemStatus.Queued, 0, 10);

        Assert.Equal(["queue-z-old", "queue-a-same", "queue-b-same", "queue-new"], queueItems.Select(queue => queue.Id).ToArray());
    }

    [Fact]
    public async Task QueueCreation_DoesNotCreateDeliveriesOrMutateWalletBalances()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var walletId = await Phase5CampaignQueueTestHelpers.SeedCompanyWalletAsync(factory.Services, company.CompanyId, company.UserId, availableBalance: 500m);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, company.CompanyId, CampaignStatus.Approved);
        await AddCampaignTargetAsync(campaignId, doctor.DoctorId);

        await Phase5CampaignQueueTestHelpers.TriggerApprovedCampaignQueueCreationAsync(factory.Services, campaignId, DateTime.UtcNow, actorUserId: company.UserId);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(candidate => candidate.Id == walletId);
        Assert.Equal(0, await context.DoctorAdDeliveries.CountAsync());
        Assert.Equal(500m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
        Assert.Equal(0, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == walletId));
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == walletId));
    }

    private async Task AddCampaignTargetAsync(string campaignId, string doctorId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var doctor = await context.DoctorProfiles.SingleAsync(profile => profile.Id == doctorId);
        await context.CampaignTargets.AddAsync(new CampaignTarget
        {
            Id = Guid.NewGuid().ToString("N"),
            CampaignId = campaignId,
            DoctorId = doctorId,
            SpecializationSnapshot = doctor.Specialization,
            ExperienceYearsSnapshot = doctor.ExperienceYears,
            LocationSnapshot = doctor.Location,
            ActivityScoreSnapshot = doctor.ActivityScore,
            PricePerMessageSnapshot = doctor.PricePerMessage ?? 0m,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private async Task AddQueueItemWithIdAsync(string queueItemId, string campaignId, string doctorId, DateTime queuedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.DoctorMessageQueues.AddAsync(new DoctorMessageQueue
        {
            Id = queueItemId,
            CampaignId = campaignId,
            DoctorId = doctorId,
            QueuedAtUtc = queuedAtUtc,
            Status = QueueItemStatus.Queued,
            CreatedAtUtc = queuedAtUtc
        });
        await context.SaveChangesAsync();
    }

    private async Task UpdateCampaignAsync(string campaignId, Action<Campaign> update)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.SingleAsync(candidate => candidate.Id == campaignId);
        update(campaign);
        await context.SaveChangesAsync();
    }

    private async Task UpdateDoctorAsync(string doctorId, Action<Core.Entities.Profiles.DoctorProfile> update)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var doctor = await context.DoctorProfiles.SingleAsync(candidate => candidate.Id == doctorId);
        update(doctor);
        await context.SaveChangesAsync();
    }

    private async Task UpdateDoctorUserStatusAsync(string doctorUserId, AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await context.Users.SingleAsync(candidate => candidate.Id == doctorUserId);
        user.AccountStatus = status;
        await context.SaveChangesAsync();
    }
}
