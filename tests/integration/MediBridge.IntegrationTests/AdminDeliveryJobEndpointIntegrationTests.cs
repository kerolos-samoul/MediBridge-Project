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

public sealed class AdminDeliveryJobEndpointIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminDeliveryJobEndpointIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetStatus_AdminOnlyReturnsRecentRunsAndDispatches()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedUserAsync(UserRole.Admin, AccountStatus.Approved);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.GetAsync("/api/admin/delivery-jobs/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.True(data.TryGetProperty("BusinessDateEgypt", out _));
        Assert.Equal(0, data.GetProperty("RecentRuns").GetArrayLength());
        Assert.Equal(0, data.GetProperty("RecentDispatches").GetArrayLength());
    }

    [Fact]
    public async Task RunInjector_ApprovedAdminEnqueuesAndAuditsReason()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedUserAsync(UserRole.Admin, AccountStatus.Approved);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PostAsJsonAsync(
            "/api/admin/delivery-jobs/run-injector",
            new { Reason = "  investigate production queue  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("DailyInjector", data.GetProperty("JobType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("SchedulerJobId").GetString()));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var audit = await context.AuditEvents.AsNoTracking().SingleAsync(item =>
            item.EventType == "ManualDailyInjectorEnqueued");
        Assert.Equal(adminUserId, audit.ActorUserId);
        Assert.Equal("investigate production queue", audit.Reason);
        Assert.Contains("DailyInjector", audit.Metadata);
    }

    [Fact]
    public async Task RunExpiry_MissingReasonReturnsBadRequestWithoutAudit()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedUserAsync(UserRole.Admin, AccountStatus.Approved);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PostAsJsonAsync(
            "/api/admin/delivery-jobs/run-expiry",
            new { Reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, await context.AuditEvents.CountAsync(item => item.EventType == "ManualDeliveryExpiryEnqueued"));
    }

    [Fact]
    public async Task RunInjector_CompanyCallerIsForbidden()
    {
        await factory.InitializeDatabaseAsync();
        var companyUserId = await SeedUserAsync(UserRole.Company, AccountStatus.Approved);
        using var client = CreateClient("Company", companyUserId);

        using var response = await client.PostAsJsonAsync(
            "/api/admin/delivery-jobs/run-injector",
            new { Reason = "Forbidden" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string role, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(role, userId));
        return client;
    }

    private async Task<string> SeedUserAsync(UserRole role, AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"delivery-job-{role.ToString().ToLowerInvariant()}-{suffix}@medibridge.local";
        var user = new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = role,
            AccountStatus = status,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
        return user.Id;
    }
}
