using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignAnalyticsIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignAnalyticsIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCampaignAnalyticsAsync_ReturnsCountsRatesAndMoneyFromLiveSources()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
        var result = await service.GetCampaignAnalyticsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13");

        Assert.Equal(4, result.DeliveredCount);
        Assert.Equal(1, result.ActiveUnansweredCount);
        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(1, result.FeedbackCount);
        Assert.Equal(50m, result.InteractionRate.Value);
        Assert.Equal(50m, result.AcceptanceRate.Value);
        Assert.Equal(50m, result.RejectionRate.Value);
        Assert.Equal(25m, result.ExpiryRate.Value);
        Assert.Equal(50m, result.FeedbackRate.Value);
        Assert.Equal(100m, result.ReservedAmount);
        Assert.Equal(200m, result.ChargedSpend);
        Assert.Equal(160m, result.DoctorEarnings);
        Assert.Equal(40m, result.PlatformFee);
    }

    [Fact]
    public async Task GetCampaignAnalyticsAsync_ZeroDenominatorRatesReturnZeroWithMetadata()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services, []);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
        var result = await service.GetCampaignAnalyticsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13");

        Assert.Equal(0m, result.InteractionRate.Value);
        Assert.False(result.InteractionRate.HasEligibleRecords);
        Assert.Equal(0m, result.FeedbackRate.Value);
        Assert.False(result.FeedbackRate.HasEligibleRecords);
    }
}
