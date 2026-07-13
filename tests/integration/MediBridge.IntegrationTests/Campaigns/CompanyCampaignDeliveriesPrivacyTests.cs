using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignDeliveriesPrivacyTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignDeliveriesPrivacyTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCampaignDeliveryReportsAsync_ReturnsOnlySafeDoctorContextAndNoWalletOrPrivateFields()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();

        var result = await service.GetCampaignDeliveryReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, null, null, null, 1, 20);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = null });

        Assert.All(result.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.PublicDoctorId));
            Assert.False(string.IsNullOrWhiteSpace(item.DoctorSpecialization));
            Assert.False(string.IsNullOrWhiteSpace(item.DoctorExperienceBand));
            Assert.False(string.IsNullOrWhiteSpace(item.DoctorLocation));
        });
        Assert.DoesNotContain("DoctorName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WalletId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Verification", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Storage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Idempotency", json, StringComparison.OrdinalIgnoreCase);
    }
}
