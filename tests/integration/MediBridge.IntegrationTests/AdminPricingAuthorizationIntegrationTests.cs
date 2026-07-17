using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminPricingAuthorizationIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminPricingAuthorizationIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task NonAdminUsersCannotSetPriceDeactivatePricingOrUpdatePlatformFee()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", company.UserId));

        using var setPrice = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price",
            new { PricePerMessage = 50m, Reason = "Forbidden" });
        using var deactivate = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price/deactivate",
            new { Reason = "Forbidden" });
        using var platformFee = await client.PutAsJsonAsync(
            "/api/admin/platform-fee-policy",
            new { FeePercent = 20m, Reason = "Forbidden" });

        Assert.Equal(HttpStatusCode.Forbidden, setPrice.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, platformFee.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(0, context.DoctorPriceHistories.Count(history => history.DoctorId == doctor.DoctorId));
        Assert.Equal(0, context.PlatformFeePolicyHistories.Count());
    }
}
