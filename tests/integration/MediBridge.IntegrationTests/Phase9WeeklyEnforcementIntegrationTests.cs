using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9WeeklyEnforcementIntegrationTests : IClassFixture<TestHost.WebAppFactory>
{
    private readonly TestHost.WebAppFactory factory;

    public Phase9WeeklyEnforcementIntegrationTests(TestHost.WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RunWeeklyEnforcement_CreatesCompliantViolationAndSuspensionSkippedDecisionsIdempotently()
    {
        await factory.InitializeDatabaseAsync();
        var weekStart = new DateOnly(2026, 7, 6);
        var compliant = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services, minimumWeeklyRequirement: 2);
        var violation = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services, minimumWeeklyRequirement: 3);
        var skipped = await Phase9TestHelpers.SeedSuspendedDoctorAsync(
            factory.Services,
            new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));
        var unapproved = await Phase9TestHelpers.SeedUnapprovedDoctorAsync(factory.Services);
        var deleted = await Phase9TestHelpers.SeedDeletedDoctorAsync(factory.Services);

        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, compliant.DoctorId, new DateOnly(2026, 7, 7), new DateTime(2026, 7, 7, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Accepted, new DateTime(2026, 7, 7, 9, 0, 0, DateTimeKind.Utc));
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, compliant.DoctorId, new DateOnly(2026, 7, 8), new DateTime(2026, 7, 8, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Rejected, new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc));
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, violation.DoctorId, new DateOnly(2026, 7, 7), new DateTime(2026, 7, 7, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Accepted, new DateTime(2026, 7, 7, 9, 0, 0, DateTimeKind.Utc));
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, unapproved.DoctorId, new DateOnly(2026, 7, 7), new DateTime(2026, 7, 7, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Accepted, new DateTime(2026, 7, 7, 9, 0, 0, DateTimeKind.Utc));
        await Phase9TestHelpers.SeedDeliveryAsync(factory.Services, deleted.DoctorId, new DateOnly(2026, 7, 7), new DateTime(2026, 7, 7, 8, 0, 0, DateTimeKind.Utc), DeliveryStatus.Accepted, new DateTime(2026, 7, 7, 9, 0, 0, DateTimeKind.Utc));
        var before = await Phase9TestHelpers.CaptureNoMutationSnapshotAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWeeklyEnforcementService>();
        await service.RunWeeklyEnforcementAsync(weekStart, null, CancellationToken.None);
        await service.RunWeeklyEnforcementAsync(weekStart, null, CancellationToken.None);

        using var assertScope = factory.Services.CreateScope();
        var context = assertScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(3, await context.WeeklyEnforcementDecisions.CountAsync(decision => decision.WeekStartDateEgypt == weekStart));
        Assert.True(await context.WeeklyEnforcementDecisions.AnyAsync(decision => decision.DoctorId == compliant.DoctorId && decision.Decision == WeeklyEnforcementDecisionType.Compliant));
        Assert.True(await context.WeeklyEnforcementDecisions.AnyAsync(decision => decision.DoctorId == violation.DoctorId && decision.Decision == WeeklyEnforcementDecisionType.Violation));
        Assert.True(await context.WeeklyEnforcementDecisions.AnyAsync(decision => decision.DoctorId == skipped.DoctorId && decision.Decision == WeeklyEnforcementDecisionType.SuspensionSkipped));
        Assert.Equal(1, await context.DoctorWeeklyViolations.CountAsync(item => item.DoctorId == violation.DoctorId && item.WeekStartDateEgypt == weekStart));
        Assert.False(await context.WeeklyEnforcementDecisions.AnyAsync(decision => decision.DoctorId == unapproved.DoctorId || decision.DoctorId == deleted.DoctorId));
        var after = await Phase9TestHelpers.CaptureNoMutationSnapshotAsync(factory.Services);
        Assert.Equal(before, after);
    }
}
