using MediBridge.IntegrationTests.Campaigns;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignReportingAuthorizationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignReportingAuthorizationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCompanyCampaignReportsAsync_DoesNotReturnCampaignsOwnedByAnotherCompany()
    {
        await factory.InitializeDatabaseAsync();
        var owner = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);
        var otherCompany = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();

        var result = await service.GetCompanyCampaignReportsAsync(
            otherCompany.CompanyUserId,
            "2026-07-01",
            "2026-07-13",
            null,
            1,
            20);

        Assert.DoesNotContain(result.Items, item => item.CampaignId == owner.CampaignId);
        Assert.Contains(result.Items, item => item.CampaignId == otherCompany.CampaignId);
    }

    [Fact]
    public async Task GetCampaignDeliveryReportsAsync_DeniesCrossCompanyCampaignWithoutRows()
    {
        await factory.InitializeDatabaseAsync();
        var owner = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);
        var otherCompany = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();

        await Assert.ThrowsAsync<Phase5NotFoundException>(() => service.GetCampaignDeliveryReportsAsync(
            otherCompany.CompanyUserId,
            owner.CampaignId,
            "2026-07-01",
            "2026-07-13",
            null,
            null,
            null,
            null,
            1,
            20));
    }

    [Fact]
    public async Task GetCampaignFeedbackReportsAsync_DeniesCrossCompanyCampaignWithoutRevealingFeedback()
    {
        await factory.InitializeDatabaseAsync();
        var owner = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);
        var otherCompany = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();

        await Assert.ThrowsAsync<Phase5NotFoundException>(() => service.GetCampaignFeedbackReportsAsync(
            otherCompany.CompanyUserId,
            owner.CampaignId,
            "2026-07-01",
            "2026-07-13",
            null,
            null,
            null,
            null,
            1,
            20));
    }
}
