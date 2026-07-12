using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9ActivityScoreIntegrationTests : IClassFixture<TestHost.WebAppFactory>
{
    private readonly TestHost.WebAppFactory factory;

    public Phase9ActivityScoreIntegrationTests(TestHost.WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RunDailyScore_ProcessesApprovedNonDeletedDoctorsAndIsIdempotentWithoutScopeMutation()
    {
        await factory.InitializeDatabaseAsync();
        var scoreDate = new DateOnly(2026, 7, 12);
        var approved = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var suspended = await Phase9TestHelpers.SeedSuspendedDoctorAsync(
            factory.Services,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));
        var unapproved = await Phase9TestHelpers.SeedUnapprovedDoctorAsync(factory.Services);
        var deleted = await Phase9TestHelpers.SeedDeletedDoctorAsync(factory.Services);
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, approved.DoctorId, new DateOnly(2026, 7, 1), new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Accepted, new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc), "Detailed clinical feedback text.");
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, suspended.DoctorId, new DateOnly(2026, 7, 2), new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Active);
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, unapproved.DoctorId, new DateOnly(2026, 7, 2), new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Accepted, new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc));
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, deleted.DoctorId, new DateOnly(2026, 7, 2), new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Accepted, new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc));
        var before = await Phase9TestHelpers.CaptureNoMutationSnapshotAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IActivityScoreService>();
        await service.RunDailyScoreAsync(scoreDate, requestedByAdminUserId: null, CancellationToken.None);
        await service.RunDailyScoreAsync(scoreDate, requestedByAdminUserId: null, CancellationToken.None);

        using var assertScope = factory.Services.CreateScope();
        var context = assertScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(2, await context.DoctorActivityScoreHistories.CountAsync(history => history.ScoreDateEgypt == scoreDate));
        Assert.True(await context.DoctorActivityScoreHistories.AnyAsync(history => history.DoctorId == approved.DoctorId && history.FinalScore > 0m));
        Assert.True(await context.DoctorActivityScoreHistories.AnyAsync(history => history.DoctorId == suspended.DoctorId && history.DoctorWasSuspended));
        Assert.False(await context.DoctorActivityScoreHistories.AnyAsync(history => history.DoctorId == unapproved.DoctorId));
        Assert.False(await context.DoctorActivityScoreHistories.AnyAsync(history => history.DoctorId == deleted.DoctorId));
        var updatedScore = await context.DoctorProfiles.Where(profile => profile.Id == approved.DoctorId).Select(profile => profile.ActivityScore).SingleAsync();
        Assert.True(updatedScore > 0m);
        var after = await Phase9TestHelpers.CaptureNoMutationSnapshotAsync(factory.Services);
        Assert.Equal(before, after);
    }
}
