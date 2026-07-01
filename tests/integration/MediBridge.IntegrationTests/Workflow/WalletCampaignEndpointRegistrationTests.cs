using System.Net;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignEndpointRegistrationTests
{
    [Fact]
    public async Task WalletCampaignEndpoints_AllContractPathsReachExpectedAuthorization()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var anonymous = factory.CreateClient();
        var resourceId = Guid.NewGuid().ToString("N");
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "/api/company/wallet"),
            new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout"),
            new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{resourceId}/assets"),
            new HttpRequestMessage(HttpMethod.Get, $"/api/company/campaigns/{resourceId}/target-preview"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{resourceId}/submit"),
            new HttpRequestMessage(HttpMethod.Get, $"/api/company/campaigns/{resourceId}/queue-summary"),
            new HttpRequestMessage(HttpMethod.Put, $"/api/admin/doctors/{resourceId}/price"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaign-assets/{resourceId}/review"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{resourceId}/review"),
            new HttpRequestMessage(HttpMethod.Get, $"/api/admin/campaigns/{resourceId}/queue")
        };

        foreach (var request in requests)
        {
            using (request)
            using (var response = await anonymous.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = document.RootElement;
                Assert.Equal(401, root.GetProperty("Code").GetInt32());
                Assert.True(root.TryGetProperty("Message", out _));
                Assert.True(root.TryGetProperty("Data", out _));
            }
        }
    }

    [Fact]
    public async Task WalletCampaignServices_AllWorkflowContractsAreRegistered()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICompanyWalletService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAdminPricingService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAdminCampaignReviewService>());
    }
}
