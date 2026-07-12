using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9JobRunIntegrationTests : IClassFixture<TestHost.WebAppFactory>
{
    private readonly TestHost.WebAppFactory factory;

    public Phase9JobRunIntegrationTests(TestHost.WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ActivityJobs_RecordTerminalCountersInterruptStaleRunsAndKeepSafeSummaries()
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var suspended = await Phase9TestHelpers.SeedSuspendedDoctorAsync(
            factory.Services,
            DateTime.UtcNow.AddDays(-3),
            DateTime.UtcNow.AddDays(-1));
        using (var seedScope = factory.Services.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            await context.ActivityEnforcementJobRuns.AddAsync(new ActivityEnforcementJobRun
            {
                Id = $"phase9-stale-{Guid.NewGuid():N}",
                JobType = ActivityEnforcementJobType.DailyActivityScore,
                TargetScoreDateEgypt = new DateOnly(2026, 7, 11),
                Status = ActivityEnforcementJobRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow.AddDays(-2),
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
            });
            await context.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var score = scope.ServiceProvider.GetRequiredService<IActivityScoreService>();
            await score.RunDailyScoreAsync(new DateOnly(2026, 7, 11), requestedByAdminUserId: null, CancellationToken.None);
            await score.ExpireSuspensionsAsync(requestedByAdminUserId: null, CancellationToken.None);
            var weekly = scope.ServiceProvider.GetRequiredService<IWeeklyEnforcementService>();
            await weekly.RunWeeklyEnforcementAsync(new DateOnly(2026, 6, 29), requestedByAdminUserId: null, CancellationToken.None);
        }

        using var assertScope = factory.Services.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var runs = await db.ActivityEnforcementJobRuns.AsNoTracking().ToListAsync();
        Assert.Contains(runs, run =>
            run.JobType == ActivityEnforcementJobType.DailyActivityScore
            && run.Status == ActivityEnforcementJobRunStatus.Interrupted
            && run.SafeFailureSummary != null
            && !run.SafeFailureSummary.Contains('\n'));
        Assert.Contains(runs, run =>
            run.JobType == ActivityEnforcementJobType.DailyActivityScore
            && run.Status == ActivityEnforcementJobRunStatus.Succeeded
            && run.ProcessedCount >= 1
            && run.CreatedCount >= 1);
        Assert.Contains(runs, run =>
            run.JobType == ActivityEnforcementJobType.SuspensionExpiry
            && run.Status == ActivityEnforcementJobRunStatus.Succeeded
            && run.UpdatedCount >= 1);
        Assert.Contains(runs, run =>
            run.JobType == ActivityEnforcementJobType.WeeklyEnforcement
            && run.Status == ActivityEnforcementJobRunStatus.Succeeded
            && run.ProcessedCount >= 1);
        Assert.True(await db.DoctorEnforcementActions.AnyAsync(action =>
            action.DoctorId == suspended.DoctorId
            && action.ActionType == DoctorEnforcementActionType.AutomaticReactivate));
        Assert.All(runs, run =>
        {
            if (run.SafeFailureSummary is null)
            {
                return;
            }

            Assert.DoesNotContain(" at ", run.SafeFailureSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password=", run.SafeFailureSummary, StringComparison.OrdinalIgnoreCase);
        });
    }
}
