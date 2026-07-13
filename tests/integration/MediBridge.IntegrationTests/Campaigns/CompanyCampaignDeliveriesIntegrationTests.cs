using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignDeliveriesIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignDeliveriesIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCampaignDeliveryReportsAsync_OrdersAndPaginatesWithoutDuplicatesOrGaps()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
        var first = await service.GetCampaignDeliveryReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, null, null, null, 1, 2);
        var second = await service.GetCampaignDeliveryReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, null, null, null, 2, 2);

        Assert.Equal(4, first.Page.TotalCount);
        var traversedIds = first.Items.Concat(second.Items).Select(item => item.DeliveryId).ToArray();
        Assert.Equal(4, traversedIds.Length);
        Assert.Equal(4, traversedIds.Distinct(StringComparer.Ordinal).Count());
        Assert.True(first.Items[0].DeliveryDateEgypt >= first.Items[1].DeliveryDateEgypt);
        Assert.True(first.Page.HasNextPage);
        Assert.True(second.Page.HasPreviousPage);
    }

    [Fact]
    public async Task GetCampaignDeliveryReportsAsync_AppliesStatusStateDoctorAndDateFilters()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();

        var accepted = await service.GetCampaignDeliveryReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", "Accepted", null, null, null, 1, 20);
        var read = await service.GetCampaignDeliveryReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, "Read", null, null, 1, 20);
        var filteredDoctor = await service.GetCampaignDeliveryReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, null, "Cardiology", "Cairo", 1, 20);
        var date = await service.GetCampaignDeliveryReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-12", "2026-07-13", null, null, null, null, 1, 20);

        Assert.Single(accepted.Items);
        Assert.Equal(DeliveryStatus.Accepted, accepted.Items[0].Status);
        Assert.Equal(2, read.Items.Count);
        Assert.Equal(4, filteredDoctor.Items.Count);
        Assert.Equal(2, date.Items.Count);
    }
}
