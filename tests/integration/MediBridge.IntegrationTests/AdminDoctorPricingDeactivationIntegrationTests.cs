using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminDoctorPricingDeactivationIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminDoctorPricingDeactivationIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task DeactivateDoctorPricing_WritesInactiveHistoryAndBlocksFutureEligibilityUntilReactivated()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 40m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", adminUserId));

        using var deactivate = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price/deactivate",
            new { Reason = "  pause paid campaign eligibility  " });

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await deactivate.Content.ReadAsStreamAsync()))
        {
            var data = document.RootElement.GetProperty("Data");
            Assert.False(data.GetProperty("pricingIsActive").GetBoolean());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("pricePerMessage").ValueKind);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var profile = await context.DoctorProfiles.AsNoTracking().SingleAsync(item => item.Id == doctor.DoctorId);
            var history = await context.DoctorPriceHistories.AsNoTracking().SingleAsync(item => item.DoctorId == doctor.DoctorId);
            Assert.False(profile.PricingIsActive);
            Assert.Equal(40m, profile.PricePerMessage);
            Assert.Equal(40m, history.PreviousPricePerMessage);
            Assert.Null(history.NewPricePerMessage);
            Assert.False(history.PricingIsActive);
            Assert.Equal(adminUserId, history.ChangedByAdminUserId);
            Assert.Equal("pause paid campaign eligibility", history.Reason);
            Assert.True(history.CreatedAtUtc > DateTime.MinValue);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
            Assert.Empty(await unitOfWork.Profiles.ListEligibleDoctorsByIdsAsync([doctor.DoctorId]));
        }

        using var reactivate = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price",
            new { PricePerMessage = 55m, Reason = "Reactivate paid campaigns" });

        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
            var eligible = await unitOfWork.Profiles.ListEligibleDoctorsByIdsAsync([doctor.DoctorId]);
            Assert.Single(eligible);
            Assert.Equal(55m, eligible[0].PricePerMessage);
        }
    }

    private async Task<string> SeedApprovedAdminAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var email = $"pricing-deactivate-admin-{Guid.NewGuid():N}@medibridge.local";
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
