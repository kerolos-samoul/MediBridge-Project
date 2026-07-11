using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminPlatformFeePolicyIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminPlatformFeePolicyIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SetPlatformFeePolicy_ClosesPreviousPolicyAddsNewPolicyAndAudits()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var previous = new PlatformFeePolicyHistory
        {
            Id = Guid.NewGuid().ToString("N"),
            FeePercent = 10m,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-7),
            ChangedByAdminUserId = adminUserId
        };
        await context.PlatformFeePolicyHistories.AddAsync(previous);
        await context.SaveChangesAsync();
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            "/api/admin/platform-fee-policy",
            new { FeePercent = 20m, Reason = "  configure production settlement fee  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(20m, data.GetProperty("FeePercent").GetDecimal());
        var newPolicyId = data.GetProperty("PolicyId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newPolicyId));

        var policies = await context.PlatformFeePolicyHistories.AsNoTracking().OrderBy(policy => policy.EffectiveFromUtc).ToListAsync();
        Assert.Equal(2, policies.Count);
        Assert.NotNull(policies[0].EffectiveToUtc);
        Assert.Equal(20m, policies[1].FeePercent);
        Assert.Equal("configure production settlement fee", policies[1].Reason);
        var audit = await context.AuditEvents.AsNoTracking().SingleAsync(item =>
            item.EventType == "PlatformFeePolicyUpdated" && item.TargetId == newPolicyId);
        Assert.Equal(adminUserId, audit.ActorUserId);
        Assert.Equal("configure production settlement fee", audit.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.01)]
    public async Task SetPlatformFeePolicy_InvalidPercentReturnsBadRequest(decimal feePercent)
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            "/api/admin/platform-fee-policy",
            new { FeePercent = feePercent, Reason = "Invalid" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetPlatformFeePolicy_CompanyCallerIsForbidden()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        using var client = CreateClient("Company", company.UserId);

        using var response = await client.PutAsJsonAsync(
            "/api/admin/platform-fee-policy",
            new { FeePercent = 20m, Reason = "Forbidden" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        var email = $"platform-fee-admin-{suffix}@medibridge.local";
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
