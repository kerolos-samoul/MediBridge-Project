using System.Net;
using System.Net.Http.Headers;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminToolsReadOnlyIntegrationTests
{
    [Fact]
    public async Task WorkQueueRead_DoesNotMutateSourceTablesOrAuditEvidence()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await AdminToolsWorkQueueIntegrationTests.SeedWorkQueueSourcesAsync(factory.Services);
        var before = await SnapshotAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin));

        using var response = await client.GetAsync("/api/admin/work-queue?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await SnapshotAsync(factory.Services);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task StatisticsRead_DoesNotMutateSourceTablesOrAuditEvidence()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await AdminStatisticsIntegrationTests.SeedStatisticsSourcesAsync(factory.Services, DateTime.UtcNow);
        var before = await SnapshotAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.GetAsync($"/api/admin/statistics?fromDateEgypt={seed.BusinessDate:yyyy-MM-dd}&toDateEgypt={seed.BusinessDate:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await SnapshotAsync(factory.Services);
        Assert.Equal(before, after);
    }

    private static async Task<ReadOnlySnapshot> SnapshotAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return new ReadOnlySnapshot(
            await db.Users.CountAsync(),
            await db.StoredFiles.CountAsync(),
            await db.Campaigns.CountAsync(),
            await db.DoctorAdDeliveries.CountAsync(),
            await db.DeliveryInteractions.CountAsync(),
            await db.WithdrawalRequests.CountAsync(),
            await db.Wallets.CountAsync(),
            await db.WalletTransactions.CountAsync(),
            await db.WalletLedgerEntries.CountAsync(),
            await db.DoctorPriceHistories.CountAsync(),
            await db.PlatformFeePolicyHistories.CountAsync(),
            await db.DoctorWeeklyViolations.CountAsync(),
            await db.DoctorEnforcementActions.CountAsync(),
            await db.AuditEvents.CountAsync());
    }

    private sealed record ReadOnlySnapshot(
        int Users,
        int StoredFiles,
        int Campaigns,
        int Deliveries,
        int Interactions,
        int WithdrawalRequests,
        int Wallets,
        int WalletTransactions,
        int WalletLedgerEntries,
        int DoctorPriceHistories,
        int PlatformFeePolicies,
        int DoctorWeeklyViolations,
        int DoctorEnforcementActions,
        int AuditEvents);
}
