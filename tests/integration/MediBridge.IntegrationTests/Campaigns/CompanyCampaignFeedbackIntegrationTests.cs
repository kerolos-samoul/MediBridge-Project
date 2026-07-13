using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignFeedbackIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignFeedbackIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCampaignFeedbackReportsAsync_ExcludesOmittedEmptyAndWhitespaceFeedback()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(
            factory.Services,
            [
                new CompanyReportingDeliverySpec("omitted", DeliveryStatus.Accepted, new DateOnly(2026, 7, 10), HasFeedback: false),
                new CompanyReportingDeliverySpec("empty", DeliveryStatus.Accepted, new DateOnly(2026, 7, 11), HasFeedback: true, FeedbackText: ""),
                new CompanyReportingDeliverySpec("whitespace", DeliveryStatus.Rejected, new DateOnly(2026, 7, 12), HasFeedback: true, FeedbackText: "   "),
                new CompanyReportingDeliverySpec("valid", DeliveryStatus.Accepted, new DateOnly(2026, 7, 13), HasFeedback: true, FeedbackText: "Useful")
            ]);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
        var result = await service.GetCampaignFeedbackReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, null, null, null, 1, 20);

        var row = Assert.Single(result.Items);
        Assert.Equal("Useful", row.FeedbackText);
    }

    [Fact]
    public async Task GetCampaignFeedbackReportsAsync_ReturnsShortNonEmptyFeedbackAsIneligible()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(
            factory.Services,
            [new CompanyReportingDeliverySpec("short", DeliveryStatus.Rejected, new DateOnly(2026, 7, 13), HasFeedback: true, FeedbackText: "Short", FeedbackQualityStatus: FeedbackQualityStatus.Flagged)]);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
        var result = await service.GetCampaignFeedbackReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", null, "Ineligible", null, null, 1, 20);

        var row = Assert.Single(result.Items);
        Assert.Equal("Short", row.FeedbackText);
        Assert.False(row.FeedbackQualifiesForScore);
    }

    [Fact]
    public async Task GetCampaignFeedbackReportsAsync_AppliesOutcomeEligibilityDoctorAndDateFilters()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
        var accepted = await service.GetCampaignFeedbackReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", "Accepted", "Eligible", "Cardiology", "Cairo", 1, 20);
        var rejected = await service.GetCampaignFeedbackReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13", "Rejected", null, null, null, 1, 20);
        var outsideDate = await service.GetCampaignFeedbackReportsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-12", "2026-07-13", null, null, null, null, 1, 20);

        Assert.Single(accepted.Items);
        Assert.Equal("Accepted", accepted.Items[0].Outcome);
        Assert.Empty(rejected.Items);
        Assert.Empty(outsideDate.Items);
    }
}
