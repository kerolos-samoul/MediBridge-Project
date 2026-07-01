using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignRejectionReleaseTests
{
    [Fact]
    public async Task AdminCampaignRejection_LeavesWalletAndFinancialRecordsUnchanged()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var seeded = await SeedReservedCampaignAsync(factory, actors.CompanyUserId, actors.CompanyProfileId, actors.DoctorProfileId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{seeded.CampaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = "Rejected", Reason = "Campaign is not suitable." })
        };
        request.Headers.Add("Idempotency-Key", "rejection-release-001");
        using var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(row => row.Id == seeded.WalletId);
        Assert.Equal(25m, wallet.AvailableBalance);
        Assert.Equal(50m, wallet.ReservedBalance);
        Assert.Equal(CampaignStatus.Rejected, (await context.Campaigns.AsNoTracking().SingleAsync(row => row.Id == seeded.CampaignId)).Status);
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(row => row.CampaignId == seeded.CampaignId));
        Assert.Equal(0, await context.WalletTransactions.CountAsync(row => row.WalletId == seeded.WalletId));
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync(row => row.WalletId == seeded.WalletId));
        Assert.Equal(0, await context.DoctorAdDeliveries.CountAsync(row => row.CampaignId == seeded.CampaignId));
        Assert.Equal(0, await context.WithdrawalRequests.CountAsync());
    }

    private static async Task<(string CampaignId, string WalletId)> SeedReservedCampaignAsync(
        WebAppFactory factory,
        string companyUserId,
        string companyId,
        string doctorId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = new Wallet
        {
            OwnerType = WalletOwnerType.Company,
            OwnerId = companyId,
            OwnerUserId = companyUserId,
            AvailableBalance = 25m,
            ReservedBalance = 50m
        };
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Rejected campaign",
            Description = "Reserved campaign awaiting moderation.",
            Status = CampaignStatus.PendingReview,
            SubmittedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        await context.Wallets.AddAsync(wallet);
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignTargets.AddAsync(new CampaignTarget
        {
            CampaignId = campaign.Id,
            DoctorId = doctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 8,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 95m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = campaign.CreatedAtUtc.AddMinutes(1)
        });
        await context.SaveChangesAsync();
        return (campaign.Id, wallet.Id);
    }
}
