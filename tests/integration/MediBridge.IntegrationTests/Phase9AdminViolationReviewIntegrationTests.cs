using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9AdminViolationReviewIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase9AdminViolationReviewIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ListViolations_ReturnsRollingSummariesEligibilityFiltersAndLatestAction()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var actionEligible = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services, dailyMessageLimit: 9, minimumWeeklyRequirement: 5, activityScore: 61.9m);
        var warningEligible = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services, dailyMessageLimit: 10, minimumWeeklyRequirement: 5, activityScore: 72.2m);
        var baseWeek = new DateOnly(2026, 7, 6);
        for (var index = 0; index < 6; index++)
        {
            await Phase9TestHelpers.SeedWeeklyViolationAsync(factory.Services, actionEligible.DoctorId, baseWeek.AddDays(index * -7), index + 1);
        }

        await Phase9TestHelpers.SeedWeeklyViolationAsync(factory.Services, warningEligible.DoctorId, baseWeek, 1);
        await SeedLatestActionAsync(actionEligible.DoctorId, adminUserId);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.GetAsync("/api/admin/violations?eligibility=ActionEligible&minRollingViolations=6&weekFrom=2026-06-01&weekTo=2026-07-06&PageNumber=1&PageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("TotalCount").GetInt32());
        var item = data.GetProperty("Items").EnumerateArray().Single();
        Assert.Equal(actionEligible.DoctorId, item.GetProperty("DoctorId").GetString());
        Assert.Equal("ActionEligible", item.GetProperty("Eligibility").GetString());
        Assert.Equal(6, item.GetProperty("RollingViolationCount").GetInt32());
        Assert.Equal("Warn", item.GetProperty("LastEnforcementAction").GetProperty("ActionType").GetString());

        using var warningResponse = await client.GetAsync("/api/admin/violations?eligibility=Warning&weekFrom=2026-07-06&weekTo=2026-07-06&PageNumber=1&PageSize=10");
        Assert.Equal(HttpStatusCode.OK, warningResponse.StatusCode);
        using var warningDocument = await JsonDocument.ParseAsync(await warningResponse.Content.ReadAsStreamAsync());
        Assert.Contains(warningDocument.RootElement.GetProperty("Data").GetProperty("Items").EnumerateArray(), row =>
            row.GetProperty("DoctorId").GetString() == warningEligible.DoctorId
            && row.GetProperty("Eligibility").GetString() == "Warning");
    }

    [Fact]
    public async Task ListViolations_ExpiresSuspensionsBeforeReadAndRejectsCrossRoleAccess()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var expiredSuspended = await Phase9TestHelpers.SeedSuspendedDoctorAsync(
            factory.Services,
            DateTime.UtcNow.AddDays(-3),
            DateTime.UtcNow.AddDays(-1));
        await Phase9TestHelpers.SeedWeeklyViolationAsync(factory.Services, expiredSuspended.DoctorId, new DateOnly(2026, 7, 6), 1);
        using var companyClient = CreateClient("Company", $"company-{Guid.NewGuid():N}");
        using var forbidden = await companyClient.GetAsync("/api/admin/violations");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var adminClient = CreateClient("Admin", adminUserId);
        using var response = await adminClient.GetAsync($"/api/admin/violations?doctorId={expiredSuspended.DoctorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var expiryRun = await context.ActivityEnforcementJobRuns
            .AsNoTracking()
            .Where(run => run.JobType == ActivityEnforcementJobType.SuspensionExpiry)
            .OrderByDescending(run => run.StartedAtUtc)
            .FirstAsync();
        Assert.True(
            expiryRun.UpdatedCount > 0,
            $"Expected suspension expiry to update at least one doctor; status={expiryRun.Status}, processed={expiryRun.ProcessedCount}, skipped={expiryRun.SkippedCount}, failed={expiryRun.FailedCount}, summary={expiryRun.SafeFailureSummary}");
        var profile = await context.DoctorProfiles.AsNoTracking().SingleAsync(profile => profile.Id == expiredSuspended.DoctorId);
        Assert.Equal(DoctorMarketplaceStatus.Active, profile.Status);
        Assert.Null(profile.SuspendedAtUtc);
        Assert.Null(profile.SuspendedUntilUtc);
        Assert.True(await context.DoctorEnforcementActions.AsNoTracking().AnyAsync(action =>
            action.DoctorId == expiredSuspended.DoctorId
            && action.ActionType == DoctorEnforcementActionType.AutomaticReactivate));
    }

    [Fact]
    public async Task ListViolations_ValidatesPaginationAndDoctorFilter()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services);
        await Phase9TestHelpers.SeedWeeklyViolationAsync(factory.Services, doctor.DoctorId, new DateOnly(2026, 6, 29), 1);
        using var client = CreateClient("Admin", adminUserId);

        using var badPage = await client.GetAsync("/api/admin/violations?PageNumber=0");
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        using var filtered = await client.GetAsync($"/api/admin/violations?doctorId={doctor.DoctorId}&PageNumber=1&PageSize=1");

        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        using var document = await JsonDocument.ParseAsync(await filtered.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("PageSize").GetInt32());
        Assert.Equal(doctor.DoctorId, data.GetProperty("Items").EnumerateArray().Single().GetProperty("DoctorId").GetString());
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
        var email = $"phase9-admin-{suffix}@medibridge.local";
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

    private async Task SeedLatestActionAsync(string doctorId, string adminUserId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.DoctorEnforcementActions.AddAsync(new DoctorEnforcementAction
        {
            Id = $"phase9-action-{Guid.NewGuid():N}",
            DoctorId = doctorId,
            ActorAdminUserId = adminUserId,
            ActionType = DoctorEnforcementActionType.Warn,
            Reason = "Previously warned for weekly violations",
            PreviousStatus = DoctorMarketplaceStatus.Active,
            NewStatus = DoctorMarketplaceStatus.Warned,
            PreviousDailyMessageLimit = 9,
            EffectiveAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}
