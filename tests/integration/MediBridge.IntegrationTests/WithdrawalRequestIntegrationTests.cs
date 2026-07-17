using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class WithdrawalRequestIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public WithdrawalRequestIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ApprovedActiveDoctorCanCreateWithdrawalAndAmountMovesFromAvailableToHeldExactlyOnce()
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        await SeedDoctorWalletAsync(doctor.DoctorId, doctor.UserId, 250m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));

        using var response = await client.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 100m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(item => item.OwnerType == WalletOwnerType.Doctor && item.OwnerId == doctor.DoctorId);
        var withdrawal = await context.WithdrawalRequests.SingleAsync(item => item.DoctorId == doctor.DoctorId);
        var transaction = await context.WalletTransactions.SingleAsync(item => item.WithdrawalRequestId == withdrawal.Id);
        var ledger = await context.WalletLedgerEntries.Where(item => item.WithdrawalRequestId == withdrawal.Id).ToListAsync();

        Assert.Equal(150m, wallet.AvailableBalance);
        Assert.Equal(100m, wallet.ReservedBalance);
        Assert.Equal(WithdrawalRequestStatus.Requested, withdrawal.Status);
        Assert.Equal(WalletTransactionType.WithdrawalHold, transaction.OperationType);
        Assert.Equal(2, ledger.Count);
        Assert.Contains(ledger, item => item.Direction == WalletLedgerEntryDirection.Debit && item.BalanceType == WalletBalanceType.Available && item.Amount == 100m);
        Assert.Contains(ledger, item => item.Direction == WalletLedgerEntryDirection.Credit && item.BalanceType == WalletBalanceType.Reserved && item.Amount == 100m);
    }

    private async Task SeedDoctorWalletAsync(string doctorId, string doctorUserId, decimal availableBalance)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        context.Wallets.Add(new Wallet
        {
            Id = $"wallet-{Guid.NewGuid():N}",
            OwnerType = WalletOwnerType.Doctor,
            OwnerId = doctorId,
            OwnerUserId = doctorUserId,
            AvailableBalance = availableBalance,
            ReservedBalance = 0m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}
