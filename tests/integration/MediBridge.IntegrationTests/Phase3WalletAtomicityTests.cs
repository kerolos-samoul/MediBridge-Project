using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3WalletAtomicityTests
{
    [Fact]
    public async Task ExecuteInTransactionAsync_RollsBackWalletBalanceTransactionAndLedgerTogether()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var walletId = $"wallet-{Guid.NewGuid():N}";
        var transactionId = $"transaction-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await unitOfWork.Wallets.AddWalletAsync(walletId, WalletOwnerType.Company, ids.CompanyProfileId, ids.CompanyUserId);
        await unitOfWork.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            await unitOfWork.Wallets.StageAvailableBalanceChangeAsync(walletId, 50m, DateTime.UtcNow, cancellationToken);
            await unitOfWork.WalletTransactions.AddTransactionAsync(transactionId, walletId, WalletTransactionType.TopUp, $"atomic-{Guid.NewGuid():N}", 50m, cancellationToken);
            await unitOfWork.WalletLedgerEntries.AddLedgerEntryAsync($"ledger-{Guid.NewGuid():N}", "missing-transaction", walletId, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, 50m, cancellationToken);
        }));

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        Assert.Equal(0m, await context.Wallets.Where(wallet => wallet.Id == walletId).Select(wallet => wallet.AvailableBalance).SingleAsync());
        Assert.False(await context.WalletTransactions.AnyAsync(transaction => transaction.Id == transactionId));
    }
}
