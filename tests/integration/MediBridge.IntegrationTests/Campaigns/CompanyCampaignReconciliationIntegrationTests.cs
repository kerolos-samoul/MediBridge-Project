using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignReconciliationIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignReconciliationIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCampaignAnalyticsAsync_DiscrepancyBlocksOnlyAffectedScopeAndCreatesSafeEvidence()
    {
        await factory.InitializeDatabaseAsync();
        var consistent = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services, consistentEvidence: true);
        var discrepant = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services, consistentEvidence: false);

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
            var good = await service.GetCampaignAnalyticsAsync(consistent.CompanyUserId, consistent.CampaignId, "2026-07-01", "2026-07-13");
            Assert.Equal(200m, good.ChargedSpend);

            await Assert.ThrowsAsync<Phase5ConflictException>(() => service.GetCampaignAnalyticsAsync(discrepant.CompanyUserId, discrepant.CampaignId, "2026-07-01", "2026-07-13"));
        }

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(audit => audit.TargetId == discrepant.CampaignId && audit.EventType == "Phase10ReportingReconciliationDiscrepancy");
        Assert.DoesNotContain("Idempotency", audit.Metadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wallet", audit.Metadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", audit.Metadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
