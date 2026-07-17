using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class WithdrawalValidationIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public WithdrawalValidationIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.123)]
    public async Task InvalidAmountsCreateNoRequestHoldTransactionOrLedger(decimal amount)
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var walletId = await WithdrawalIntegrationTestHelpers.SeedDoctorWalletAsync(factory.Services, doctor.DoctorId, doctor.UserId, 100m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));

        using var response = await client.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoFinancialEffectsAsync(doctor.DoctorId, walletId, expectedAvailable: 100m, expectedReserved: 0m);
    }

    [Fact]
    public async Task InsufficientWithdrawableEarningsCreateNoRequestHoldTransactionOrLedger()
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var walletId = await WithdrawalIntegrationTestHelpers.SeedDoctorWalletAsync(factory.Services, doctor.DoctorId, doctor.UserId, availableBalance: 25m, reservedBalance: 75m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));

        using var response = await client.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 50m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNoFinancialEffectsAsync(doctor.DoctorId, walletId, expectedAvailable: 25m, expectedReserved: 75m);
    }

    private async Task AssertNoFinancialEffectsAsync(string doctorId, string walletId, decimal expectedAvailable, decimal expectedReserved)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId);
        Assert.Equal(expectedAvailable, wallet.AvailableBalance);
        Assert.Equal(expectedReserved, wallet.ReservedBalance);
        Assert.False(await context.WithdrawalRequests.AnyAsync(item => item.DoctorId == doctorId));
        Assert.False(await context.WalletTransactions.AnyAsync(item => item.WithdrawalRequestId != null));
        Assert.False(await context.WalletLedgerEntries.AnyAsync(item => item.WithdrawalRequestId != null));
    }
}
