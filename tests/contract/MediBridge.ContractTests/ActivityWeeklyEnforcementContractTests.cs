using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class ActivityWeeklyEnforcementContractTests
{
    internal const string OpenApiPath = "specs/011-activity-weekly-enforcement/contracts/activity-weekly-enforcement-api.yaml";

    internal const string ViolationsRoute = "/api/admin/violations";
    internal const string DoctorStatusRouteTemplate = "/api/admin/doctors/{doctorId}/status";
    internal const string ActivityJobStatusRoute = "/api/admin/activity-jobs/status";
    internal const string RunDailyActivityScoreRoute = "/api/admin/activity-jobs/run-score";
    internal const string RunWeeklyEnforcementRoute = "/api/admin/activity-jobs/run-weekly-enforcement";
    internal const string RunSuspensionExpiryRoute = "/api/admin/activity-jobs/run-suspension-expiry";

    [Fact]
    public async Task GetAdminViolations_ReturnsPagedAdminEnvelopeAndSafeFields()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminDoctorAndViolationAsync(factory, violationCount: 6);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.GetAsync($"{ViolationsRoute}?doctorId={seed.DoctorId}&eligibility=ActionEligible&weekFrom=2026-06-01&weekTo=2026-07-06&minRollingViolations=1&PageNumber=1&PageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("wallet", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settlement", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(1, data.GetProperty("PageNumber").GetInt32());
        Assert.Equal(100, data.GetProperty("PageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("TotalCount").GetInt32());
        var item = data.GetProperty("Items").EnumerateArray().Single();
        Assert.Equal(seed.DoctorId, item.GetProperty("DoctorId").GetString());
        Assert.Equal("ActionEligible", item.GetProperty("Eligibility").GetString());
        Assert.Equal(6, item.GetProperty("RollingViolationCount").GetInt32());
        Assert.True(item.TryGetProperty("RecentViolationWeeks", out _));
    }

    [Fact]
    public async Task GetAdminViolations_RequiresAdminAndValidPaginationEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var anonymousClient = factory.CreateClient();

        using var unauthenticated = await anonymousClient.GetAsync(ViolationsRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var forbiddenClient = factory.CreateClient();
        forbiddenClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Company"));
        using var forbidden = await forbiddenClient.GetAsync(ViolationsRoute);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var adminClient = factory.CreateClient();
        var seed = await SeedAdminDoctorAndViolationAsync(factory, violationCount: 1);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));
        using var badRequest = await adminClient.GetAsync($"{ViolationsRoute}?PageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);
        using var document = await JsonDocument.ParseAsync(await badRequest.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);

        using var badEligibility = await adminClient.GetAsync($"{ViolationsRoute}?eligibility=Escalate");
        Assert.Equal(HttpStatusCode.BadRequest, badEligibility.StatusCode);
        using var badWeek = await adminClient.GetAsync($"{ViolationsRoute}?weekFrom=2026-07-07");
        Assert.Equal(HttpStatusCode.BadRequest, badWeek.StatusCode);
    }

    [Fact]
    public async Task PutAdminDoctorStatus_AcceptsManualActionsAndReturnsActionEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminDoctorAndViolationAsync(factory, violationCount: 1);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        using var warn = await client.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                ActionType = "Warn",
                Reason = "Contract warning reason"
            }));

        Assert.Equal(HttpStatusCode.OK, warn.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await warn.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal(seed.DoctorId, data.GetProperty("DoctorId").GetString());
            Assert.Equal("Warn", data.GetProperty("ActionType").GetString());
            Assert.True(data.TryGetProperty("AuditEventId", out _));
        }

        using var reduce = await client.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                ActionType = "ReduceDailyLimit",
                Reason = "Contract reduce limit reason",
                NewDailyMessageLimit = 3
            }));
        Assert.Equal(HttpStatusCode.OK, reduce.StatusCode);

        using var suspend = await client.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                ActionType = "Suspend",
                Reason = "Contract suspension reason",
                SuspendedUntilUtc = DateTime.UtcNow.AddDays(2)
            }));
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        using var reactivate = await client.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                ActionType = "Reactivate",
                Reason = "Contract reactivation reason"
            }));
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
    }

    [Fact]
    public async Task PutAdminDoctorStatus_ValidatesAuthorizationPayloadAndTransitions()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminDoctorAndViolationAsync(factory, violationCount: 1);
        using var anonymousClient = factory.CreateClient();

        using var unauthenticated = await anonymousClient.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new { ActionType = "Warn", Reason = "Contract reason" }));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var forbiddenClient = factory.CreateClient();
        forbiddenClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor"));
        using var forbidden = await forbiddenClient.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new { ActionType = "Warn", Reason = "Contract reason" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));
        using var badReason = await adminClient.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new { ActionType = "Warn", Reason = " short " }));
        Assert.Equal(HttpStatusCode.BadRequest, badReason.StatusCode);

        using var invalidExtraValue = await adminClient.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new { ActionType = "Warn", Reason = "Valid warning reason", NewDailyMessageLimit = 2 }));
        Assert.Equal(HttpStatusCode.BadRequest, invalidExtraValue.StatusCode);

        using var pastSuspension = await adminClient.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new { ActionType = "Suspend", Reason = "Valid suspension reason", SuspendedUntilUtc = DateTime.UtcNow.AddMinutes(-5) }));
        Assert.Equal(HttpStatusCode.BadRequest, pastSuspension.StatusCode);

        using var unknown = await adminClient.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", $"missing-{Guid.NewGuid():N}", StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new { ActionType = "Warn", Reason = "Valid unknown doctor reason" }));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var conflict = await adminClient.PutAsync(
            DoctorStatusRouteTemplate.Replace("{doctorId}", seed.DoctorId, StringComparison.Ordinal),
            Phase5ContractTestHelpers.CreateJsonContent(new { ActionType = "Reactivate", Reason = "Valid conflict reason" }));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    private static async Task<ActivityContractSeed> SeedAdminDoctorAndViolationAsync(ContractWebAppFactory factory, int violationCount)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var admin = CreateUser($"phase9-admin-{suffix}@example.test", UserRole.Admin);
        var doctorUser = CreateUser($"phase9-doctor-{suffix}@example.test", UserRole.Doctor);
        var doctor = new DoctorProfile
        {
            Id = $"phase9-doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            PricePerMessage = 50m,
            DailyMessageLimit = 10,
            MinimumWeeklyRequirement = 5,
            ActivityScore = 72.5m,
            Status = DoctorMarketplaceStatus.Active,
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };

        await context.Users.AddRangeAsync(admin, doctorUser);
        await context.DoctorProfiles.AddAsync(doctor);
        var weekStart = new DateOnly(2026, 7, 6);
        for (var index = 0; index < violationCount; index++)
        {
            var currentWeek = weekStart.AddDays(index * -7);
            var decisionId = $"phase9-decision-{suffix}-{index}";
            await context.WeeklyEnforcementDecisions.AddAsync(new WeeklyEnforcementDecision
            {
                Id = decisionId,
                DoctorId = doctor.Id,
                WeekStartDateEgypt = currentWeek,
                WeekEndDateEgypt = currentWeek.AddDays(7),
                MinimumWeeklyRequirement = 5,
                InteractionCount = 0,
                Decision = WeeklyEnforcementDecisionType.Violation,
                RollingViolationCountAfterDecision = index + 1,
                CreatedAtUtc = DateTime.UtcNow
            });
            await context.DoctorWeeklyViolations.AddAsync(new DoctorWeeklyViolation
            {
                Id = $"phase9-violation-{suffix}-{index}",
                DoctorId = doctor.Id,
                WeeklyEnforcementDecisionId = decisionId,
                WeekStartDateEgypt = currentWeek,
                WeekEndDateEgypt = currentWeek.AddDays(7),
                MinimumWeeklyRequirement = 5,
                InteractionCount = 0,
                RollingViolationCount = index + 1,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return new ActivityContractSeed(admin.Id, doctor.Id);
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role)
    {
        return new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = role,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private sealed record ActivityContractSeed(string AdminUserId, string DoctorId);
}
