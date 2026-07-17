using System.Net;
using System.Net.Http.Headers;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminToolsPrivacyIntegrationTests
{
    private static readonly string[] ForbiddenFragments =
    [
        "storage-key-secret",
        "provider-secret",
        "Idempotency",
        "Destination",
        "StackTrace",
        "Phone",
        "Email",
        "walletId",
        "availableBalance",
        "reservedBalance",
        "concurrencyToken",
        "bankAccount",
        "providerCredential"
    ];

    [Fact]
    public async Task WorkQueueResponse_ExcludesSensitiveOperationalFields()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await AdminToolsWorkQueueIntegrationTests.SeedWorkQueueSourcesAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin));

        using var response = await client.GetAsync("/api/admin/work-queue?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        AssertNoForbiddenFragments(json);
    }

    [Fact]
    public async Task StatisticsResponse_ExcludesSensitiveOperationalFields()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await AdminStatisticsIntegrationTests.SeedStatisticsSourcesAsync(factory.Services, DateTime.UtcNow);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.GetAsync($"/api/admin/statistics?fromDateEgypt={seed.BusinessDate:yyyy-MM-dd}&toDateEgypt={seed.BusinessDate:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoForbiddenFragments(await response.Content.ReadAsStringAsync());
    }

    private static void AssertNoForbiddenFragments(string json)
    {
        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
