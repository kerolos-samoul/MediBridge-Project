using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9JobConcurrencyIntegrationTests : IClassFixture<TestHost.WebAppFactory>
{
    private readonly TestHost.WebAppFactory factory;

    public Phase9JobConcurrencyIntegrationTests(TestHost.WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ConcurrentRuns_ConvergeOnUniqueSnapshotsDecisionsViolationsAndAutomaticReactivation()
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services, minimumWeeklyRequirement: 2);
        var suspended = await Phase9TestHelpers.SeedSuspendedDoctorAsync(
            factory.Services,
            DateTime.UtcNow.AddDays(-2),
            DateTime.UtcNow.AddDays(-1));

        await Task.WhenAll(
            RunDailyScoreAsync(new DateOnly(2026, 7, 11)),
            RunDailyScoreAsync(new DateOnly(2026, 7, 11)));
        await Task.WhenAll(
            RunWeeklyAsync(new DateOnly(2026, 6, 29)),
            RunWeeklyAsync(new DateOnly(2026, 6, 29)));
        await Task.WhenAll(
            RunSuspensionExpiryAsync(),
            RunSuspensionExpiryAsync());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await context.DoctorActivityScoreHistories.CountAsync(snapshot =>
            snapshot.DoctorId == doctor.DoctorId
            && snapshot.ScoreDateEgypt == new DateOnly(2026, 7, 11)));
        Assert.Equal(1, await context.WeeklyEnforcementDecisions.CountAsync(decision =>
            decision.DoctorId == doctor.DoctorId
            && decision.WeekStartDateEgypt == new DateOnly(2026, 6, 29)));
        Assert.Equal(1, await context.DoctorWeeklyViolations.CountAsync(violation =>
            violation.DoctorId == doctor.DoctorId
            && violation.WeekStartDateEgypt == new DateOnly(2026, 6, 29)));
        Assert.Equal(1, await context.DoctorEnforcementActions.CountAsync(action =>
            action.DoctorId == suspended.DoctorId
            && action.ActionType == DoctorEnforcementActionType.AutomaticReactivate));
        Assert.True(await context.ActivityEnforcementJobRuns.CountAsync(run =>
            run.JobType == ActivityEnforcementJobType.DailyActivityScore
            && run.TargetScoreDateEgypt == new DateOnly(2026, 7, 11)
            && run.Status != ActivityEnforcementJobRunStatus.Running) >= 2);
    }

    private async Task RunDailyScoreAsync(DateOnly scoreDate)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IActivityScoreService>()
            .RunDailyScoreAsync(scoreDate, requestedByAdminUserId: null, CancellationToken.None);
    }

    private async Task RunWeeklyAsync(DateOnly weekStart)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IWeeklyEnforcementService>()
            .RunWeeklyEnforcementAsync(weekStart, requestedByAdminUserId: null, CancellationToken.None);
    }

    private async Task RunSuspensionExpiryAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IActivityScoreService>()
            .ExpireSuspensionsAsync(requestedByAdminUserId: null, CancellationToken.None);
    }
}
