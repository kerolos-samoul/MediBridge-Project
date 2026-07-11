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

public sealed class AdminDoctorDeliverySettingsIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminDoctorDeliverySettingsIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SetDoctorDeliverySettings_UpdatesApprovedDoctorAndAuditsReason()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 40m, dailyMessageLimit: 1);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/delivery-settings",
            new { DailyMessageLimit = 6, MinimumWeeklyRequirement = 3, Reason = "  configure operational capacity  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(doctor.DoctorId, data.GetProperty("DoctorId").GetString());
        Assert.Equal(6, data.GetProperty("DailyMessageLimit").GetInt32());
        Assert.Equal(3, data.GetProperty("MinimumWeeklyRequirement").GetInt32());
        Assert.True(data.GetProperty("IsDeliveryEligible").GetBoolean());
        Assert.Equal("DeliveryEligible", data.GetProperty("EligibilityState").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var profile = await context.DoctorProfiles.AsNoTracking().SingleAsync(item => item.Id == doctor.DoctorId);
        Assert.Equal(6, profile.DailyMessageLimit);
        Assert.Equal(3, profile.MinimumWeeklyRequirement);
        var audit = await context.AuditEvents.AsNoTracking().SingleAsync(item =>
            item.EventType == "DoctorDeliverySettingsUpdated" && item.TargetId == doctor.DoctorId);
        Assert.Equal(adminUserId, audit.ActorUserId);
        Assert.Equal("configure operational capacity", audit.Reason);
        Assert.Contains("\"NewDailyMessageLimit\":6", audit.Metadata);
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item =>
            item.OwnerType == WalletOwnerType.Doctor && item.OwnerId == doctor.DoctorId);
        Assert.Equal(doctor.UserId, wallet.OwnerUserId);
        Assert.Equal(0m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
    }

    [Fact]
    public async Task GetDoctorDeliverySettings_ReportsMissingPriceAsNotEligible()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 0m, dailyMessageLimit: 10);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.GetAsync($"/api/admin/doctors/{doctor.DoctorId}/delivery-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.False(data.GetProperty("IsDeliveryEligible").GetBoolean());
        Assert.Equal("DoctorPriceMissing", data.GetProperty("EligibilityState").GetString());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(101, 1)]
    [InlineData(1, 701)]
    public async Task SetDoctorDeliverySettings_InvalidLimitsReturnBadRequest(int dailyLimit, int weeklyRequirement)
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, dailyMessageLimit: 5);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/delivery-settings",
            new { DailyMessageLimit = dailyLimit, MinimumWeeklyRequirement = weeklyRequirement, Reason = "Invalid settings" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var profile = await context.DoctorProfiles.AsNoTracking().SingleAsync(item => item.Id == doctor.DoctorId);
        Assert.Equal(5, profile.DailyMessageLimit);
        Assert.Equal(0, await context.AuditEvents.CountAsync(item =>
            item.EventType == "DoctorDeliverySettingsUpdated" && item.TargetId == doctor.DoctorId));
    }

    [Fact]
    public async Task SetDoctorDeliverySettings_UnknownDoctorReturnsNotFound()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{Guid.NewGuid():N}/delivery-settings",
            new { DailyMessageLimit = 5, MinimumWeeklyRequirement = 1, Reason = "Unknown" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDoctorDeliverySettings_CompanyCallerIsForbidden()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        using var client = CreateClient("Company", company.UserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/delivery-settings",
            new { DailyMessageLimit = 5, MinimumWeeklyRequirement = 1, Reason = "Forbidden" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetDoctorDeliverySettings_SuspendedMarketplaceDoctorReturnsConflict()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedSuspendedDoctorAsync(factory.Services);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/delivery-settings",
            new { DailyMessageLimit = 5, MinimumWeeklyRequirement = 1, Reason = "Suspended" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
        var email = $"delivery-settings-admin-{suffix}@medibridge.local";
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
