using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignReportingReadOnlyTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CompanyCampaignReportingReadOnlyTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCampaignAnalyticsAsync_IsReadOnlyWhenSourcesReconcile()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await CompanyReportingIntegrationTestHelpers.SeedCampaignAsync(factory.Services);
        Counts before;
        using (var scope = factory.Services.CreateScope())
        {
            before = await CountAsync(scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>());
        }

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICompanyReportingService>();
            _ = await service.GetCampaignAnalyticsAsync(seed.CompanyUserId, seed.CampaignId, "2026-07-01", "2026-07-13");
        }

        using var verificationScope = factory.Services.CreateScope();
        var after = await CountAsync(verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>());
        Assert.Equal(before, after);
    }

    private static async Task<Counts> CountAsync(MediBridgeDbContext db)
        => new(
            await db.Campaigns.CountAsync(),
            await db.DoctorAdDeliveries.CountAsync(),
            await db.Wallets.CountAsync(),
            await db.WalletTransactions.CountAsync(),
            await db.WalletLedgerEntries.CountAsync(),
            await db.DoctorMessageQueues.CountAsync(),
            await db.DeliveryJobRuns.CountAsync(),
            await db.AuditEvents.CountAsync());

    private sealed record Counts(
        int Campaigns,
        int Deliveries,
        int Wallets,
        int WalletTransactions,
        int WalletLedgerEntries,
        int Queues,
        int Jobs,
        int Audits);
}
