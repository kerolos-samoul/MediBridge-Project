using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9AdminEnforcementActionIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase9AdminEnforcementActionIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ApplyDoctorEnforcementAction_AppendsActionsAuditAndUpdatesProfileWithoutMutatingWalletQueueOrDeliveries()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services, dailyMessageLimit: 10);
        await Phase9TestHelpers.SeedWeeklyViolationAsync(factory.Services, doctor.DoctorId, new DateOnly(2026, 7, 6), 1);
        var before = await Phase9TestHelpers.CaptureNoMutationSnapshotAsync(factory.Services);
        using var client = CreateClient("Admin", adminUserId);

        using var warn = await client.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "Warn",
            Reason = "Weekly activity warning reason"
        });
        Assert.Equal(HttpStatusCode.OK, warn.StatusCode);

        using var reduce = await client.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "ReduceDailyLimit",
            Reason = "Weekly activity reduce limit reason",
            NewDailyMessageLimit = 4
        });
        Assert.Equal(HttpStatusCode.OK, reduce.StatusCode);

        using var suspend = await client.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "Suspend",
            Reason = "Weekly activity suspension reason",
            SuspendedUntilUtc = DateTime.UtcNow.AddDays(2)
        });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        using var reactivate = await client.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "Reactivate",
            Reason = "Weekly activity manual reactivation reason"
        });
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        using var document = await JsonDocument.ParseAsync(await reactivate.Content.ReadAsStreamAsync());
        Assert.Equal(200, document.RootElement.GetProperty("Code").GetInt32());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(doctor.DoctorId, data.GetProperty("DoctorId").GetString());
        Assert.Equal("Active", data.GetProperty("Status").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var profile = await context.DoctorProfiles.AsNoTracking().SingleAsync(profile => profile.Id == doctor.DoctorId);
        Assert.Equal(DoctorMarketplaceStatus.Active, profile.Status);
        Assert.Equal(4, profile.DailyMessageLimit);
        Assert.Null(profile.SuspendedAtUtc);
        Assert.Null(profile.SuspendedUntilUtc);
        Assert.Equal(4, await context.DoctorEnforcementActions.CountAsync(action => action.DoctorId == doctor.DoctorId));
        Assert.Equal(4, await context.AuditEvents.CountAsync(audit => audit.TargetId == doctor.DoctorId && audit.EventType == "Phase9DoctorEnforcementActionApplied"));
        Assert.Equal(1, await context.DoctorWeeklyViolations.CountAsync(violation => violation.DoctorId == doctor.DoctorId));
        var after = await Phase9TestHelpers.CaptureNoMutationSnapshotAsync(factory.Services);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ApplyDoctorEnforcementAction_RejectsInvalidPayloadsUnknownDoctorsAndInvalidTransitions()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var suspended = await Phase9TestHelpers.SeedSuspendedDoctorAsync(
            factory.Services,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(1));
        using var client = CreateClient("Admin", adminUserId);

        using var missingReason = await client.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "Warn",
            Reason = "   "
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        using var paddedShortReason = await client.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "Warn",
            Reason = "  short  "
        });
        Assert.Equal(HttpStatusCode.BadRequest, paddedShortReason.StatusCode);

        using var invalidTransition = await client.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "Reactivate",
            Reason = "Reactivation requires suspended doctor"
        });
        Assert.Equal(HttpStatusCode.Conflict, invalidTransition.StatusCode);

        using var warnSuspended = await client.PutAsJsonAsync($"/api/admin/doctors/{suspended.DoctorId}/status", new
        {
            ActionType = "Warn",
            Reason = "Suspended doctor warning conflict"
        });
        Assert.Equal(HttpStatusCode.Conflict, warnSuspended.StatusCode);

        using var unknown = await client.PutAsJsonAsync($"/api/admin/doctors/missing-{Guid.NewGuid():N}/status", new
        {
            ActionType = "Warn",
            Reason = "Unknown doctor reason"
        });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(DoctorMarketplaceStatus.Active, (await context.DoctorProfiles.AsNoTracking().SingleAsync(profile => profile.Id == doctor.DoctorId)).Status);
        Assert.Equal(0, await context.DoctorEnforcementActions.CountAsync(action => action.DoctorId == doctor.DoctorId));
    }

    [Fact]
    public async Task ApplyDoctorEnforcementAction_RequiresAdmin()
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services);
        using var companyClient = CreateClient("Company", $"company-{Guid.NewGuid():N}");

        using var forbidden = await companyClient.PutAsJsonAsync($"/api/admin/doctors/{doctor.DoctorId}/status", new
        {
            ActionType = "Warn",
            Reason = "Company cannot enforce doctor status"
        });

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private HttpClient CreateClient(string role, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(role, userId));
        return client;
    }

    private async Task<string> SeedApprovedAdminAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"phase9-action-admin-{suffix}@medibridge.local";
        var user = new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
        return user.Id;
    }
}
