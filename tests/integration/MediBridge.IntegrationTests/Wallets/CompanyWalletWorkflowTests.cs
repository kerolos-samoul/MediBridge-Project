using System.Net;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Wallets;

public sealed class CompanyWalletWorkflowTests
{
    [Fact]
    public async Task CompanyWalletQuery_CreatesMissingWalletForApprovedCompany()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await client.GetAsync("/api/company/wallet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallets = await context.Wallets
            .AsNoTracking()
            .Where(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == ids.CompanyProfileId && !wallet.IsDeleted)
            .ToListAsync();
        var wallet = Assert.Single(wallets);
        Assert.Equal(0m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
        Assert.Equal("EGP", wallet.Currency);
    }
}
