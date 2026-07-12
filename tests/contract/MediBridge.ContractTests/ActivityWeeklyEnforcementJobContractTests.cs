using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class ActivityWeeklyEnforcementJobContractTests
{
    [Fact]
    public async Task ActivityJobEndpoints_RequireAdminAndReturnSafeStatusEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var anonymousClient = factory.CreateClient();

        using var anonymous = await anonymousClient.GetAsync(ActivityWeeklyEnforcementContractTests.ActivityJobStatusRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor"));
        using var forbidden = await doctorClient.GetAsync(ActivityWeeklyEnforcementContractTests.ActivityJobStatusRoute);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var adminUserId = await SeedAdminAsync(factory);
        using var adminClient = CreateAdminClient(factory, adminUserId);
        using var status = await adminClient.GetAsync(ActivityWeeklyEnforcementContractTests.ActivityJobStatusRoute);

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var document = await JsonDocument.ParseAsync(await status.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
    }

    [Fact]
    public async Task RunDailyScoreAndWeeklyEnforcement_ValidateDatesAndReturnJobRunEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedAdminAsync(factory);
        using var client = CreateAdminClient(factory, adminUserId);

        using var badScore = await client.PostAsync(
            ActivityWeeklyEnforcementContractTests.RunDailyActivityScoreRoute,
            Phase5ContractTestHelpers.CreateJsonContent(new { ScoreDateEgypt = "2999-01-01" }));
        Assert.Equal(HttpStatusCode.BadRequest, badScore.StatusCode);

        using var score = await client.PostAsync(
            ActivityWeeklyEnforcementContractTests.RunDailyActivityScoreRoute,
            Phase5ContractTestHelpers.CreateJsonContent(new { ScoreDateEgypt = "2026-07-11" }));
        Assert.Equal(HttpStatusCode.OK, score.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await score.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal("DailyActivityScore", data.GetProperty("JobType").GetString());
            Assert.Equal("2026-07-11", data.GetProperty("TargetScoreDateEgypt").GetString());
        }

        using var badWeek = await client.PostAsync(
            ActivityWeeklyEnforcementContractTests.RunWeeklyEnforcementRoute,
            Phase5ContractTestHelpers.CreateJsonContent(new { WeekStartDateEgypt = "2026-07-07" }));
        Assert.Equal(HttpStatusCode.BadRequest, badWeek.StatusCode);

        using var weekly = await client.PostAsync(
            ActivityWeeklyEnforcementContractTests.RunWeeklyEnforcementRoute,
            Phase5ContractTestHelpers.CreateJsonContent(new { WeekStartDateEgypt = "2026-06-29" }));
        Assert.Equal(HttpStatusCode.OK, weekly.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await weekly.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal("WeeklyEnforcement", data.GetProperty("JobType").GetString());
            Assert.Equal("2026-06-29", data.GetProperty("TargetWeekStartDateEgypt").GetString());
        }
    }

    [Fact]
    public async Task RunSuspensionExpiry_ReturnsJobRunWithNullTargets()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedAdminAsync(factory);
        using var client = CreateAdminClient(factory, adminUserId);

        using var response = await client.PostAsync(ActivityWeeklyEnforcementContractTests.RunSuspensionExpiryRoute, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal("SuspensionExpiry", data.GetProperty("JobType").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("TargetScoreDateEgypt").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("TargetWeekStartDateEgypt").ValueKind);
        Assert.DoesNotContain("stack", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateAdminClient(ContractWebAppFactory factory, string adminUserId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Admin", adminUserId));
        return client;
    }

    private static async Task<string> SeedAdminAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"phase9-job-admin-{suffix}@example.test";
        var admin = new MediBridgeIdentityUser
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

        await context.Users.AddAsync(admin);
        await context.SaveChangesAsync();
        return admin.Id;
    }
}
