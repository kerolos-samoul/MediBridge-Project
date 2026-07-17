using System.Net;
using System.Net.Http.Headers;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminStatisticsAuthorizationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminStatisticsAuthorizationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task NonAdminUsersCannotAccessStatisticsAndReceiveNoMetrics()
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.GetAsync("/api/admin/statistics");

        using var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));
        using var forbidden = await doctorClient.GetAsync("/api/admin/statistics");
        var unauthorizedJson = await unauthorized.Content.ReadAsStringAsync();
        var forbiddenJson = await forbidden.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.DoesNotContain("accountCounts", unauthorizedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("walletMovementSummary", forbiddenJson, StringComparison.OrdinalIgnoreCase);
    }
}
