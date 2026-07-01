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

public sealed class AdminDoctorPricingIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminDoctorPricingIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SetDoctorPrice_UpdatesApprovedDoctorAndAppendsPriceHistory()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 40m);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price",
            new { PricePerMessage = 64.25m, Reason = "  Initial campaign rate  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(doctor.DoctorId, data.GetProperty("doctorId").GetString());
        Assert.Equal(64.25m, data.GetProperty("pricePerMessage").GetDecimal());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var profile = await context.DoctorProfiles.AsNoTracking().SingleAsync(item => item.Id == doctor.DoctorId);
        var history = await context.DoctorPriceHistories.AsNoTracking().SingleAsync(item => item.DoctorId == doctor.DoctorId);
        Assert.Equal(64.25m, profile.PricePerMessage);
        Assert.Equal(40m, history.PreviousPricePerMessage);
        Assert.Equal(64.25m, history.NewPricePerMessage);
        Assert.Equal(adminUserId, history.ChangedByAdminUserId);
        Assert.Equal("Initial campaign rate", history.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.001)]
    public async Task SetDoctorPrice_InvalidPriceReturnsBadRequestWithoutHistory(decimal price)
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 40m);
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price",
            new { PricePerMessage = price, Reason = "Invalid" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(40m, (await context.DoctorProfiles.AsNoTracking().SingleAsync(item => item.Id == doctor.DoctorId)).PricePerMessage);
        Assert.Equal(0, await context.DoctorPriceHistories.CountAsync(item => item.DoctorId == doctor.DoctorId));
    }

    [Fact]
    public async Task SetDoctorPrice_UnknownDoctorReturnsNotFound()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        using var client = CreateClient("Admin", adminUserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{Guid.NewGuid():N}/price",
            new { PricePerMessage = 50m, Reason = "Unknown" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDoctorPrice_CompanyCallerIsForbidden()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        using var client = CreateClient("Company", company.UserId);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price",
            new { PricePerMessage = 50m, Reason = "Forbidden" });

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
        var email = $"pricing-admin-{suffix}@medibridge.local";
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
