using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.Campaigns;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignReportSummaryIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignReportSummaryIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCompanyCampaignReportsAsync_ReturnsOwnedMixedStateTotalsOrderedByActivity()
    {
        await factory.InitializeDatabaseAsync();
        var newer = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);
        var older = await CompanyReportingIntegrationTestHelpers.SeedAdditionalCampaignForCompanyAsync(
            factory.Services,
            newer,
            [new CompanyReportingDeliverySpec("older", DeliveryStatus.Accepted, new DateOnly(2026, 7, 2), HasFeedback: false)]);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();

        var result = await service.GetCompanyCampaignReportsAsync(
            newer.CompanyUserId,
            "2026-07-01",
            "2026-07-13",
            null,
            1,
            20);

        Assert.Equal(2, result.Page.TotalCount);
        Assert.Equal(2, result.Items.Count);
        var item = result.Items[0];
        Assert.Equal(newer.CampaignId, item.CampaignId);
        Assert.Equal(4, item.DeliveredCount);
        Assert.Equal(1, item.ActiveUnansweredCount);
        Assert.Equal(1, item.AcceptedCount);
        Assert.Equal(1, item.RejectedCount);
        Assert.Equal(1, item.ExpiredCount);
        Assert.Equal(1, item.FeedbackCount);
        Assert.Equal(100m, item.ReservedAmount);
        Assert.Equal(200m, item.ChargedSpend);
        Assert.Equal(160m, item.DoctorEarnings);
        Assert.Equal(40m, item.PlatformFee);
        Assert.Equal(older.CampaignId, result.Items[1].CampaignId);
    }

    [Fact]
    public async Task GetCompanyCampaignReportsAsync_EmptyStatusFilterReturnsEmptyPageWithZeroTotals()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services, status: CampaignStatus.Active);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();

        var result = await service.GetCompanyCampaignReportsAsync(
            seed.CompanyUserId,
            "2026-07-01",
            "2026-07-13",
            "Draft",
            1,
            20);

        Assert.Equal(0, result.Page.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetCompanyCampaignReportsAsync_IgnoresStoredAggregatesAndBlocksDiscrepantSourceEvidence()
    {
        await factory.InitializeDatabaseAsync();
        var consistent = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services, consistentEvidence: true);
        var discrepant = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services, consistentEvidence: false);

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
            var result = await service.GetCompanyCampaignReportsAsync(
                consistent.CompanyUserId,
                "2026-07-01",
                "2026-07-13",
                null,
                1,
                20);
            Assert.Single(result.Items);
            Assert.Equal(200m, result.Items[0].ChargedSpend);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
            await Assert.ThrowsAsync<Phase5ConflictException>(() => service.GetCompanyCampaignReportsAsync(
                discrepant.CompanyUserId,
                "2026-07-01",
                "2026-07-13",
                null,
                1,
                20));
        }

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(audit =>
            audit.TargetId == discrepant.CampaignId &&
            audit.EventType == "Phase10ReportingReconciliationDiscrepancy");
        Assert.DoesNotContain("IdempotencyKey", audit.Metadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wallet", audit.Metadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", audit.Metadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
