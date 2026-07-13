using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignFeedbackPrivacyTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignFeedbackPrivacyTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCampaignFeedbackReportsAsync_ReturnsNoDoctorPrivateWalletOrIdempotencyFields()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
        var result = await service.GetCampaignFeedbackReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, null, null, null, 1, 20);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = null });

        Assert.DoesNotContain("DoctorName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wallet", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Verification", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Idempotency", json, StringComparison.OrdinalIgnoreCase);
    }
}
