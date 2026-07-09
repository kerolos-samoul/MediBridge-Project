using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3WalletLedgerFlowTests
{
    [Fact]
    public async Task WalletRepository_SupportsRequiredPhase3LedgerFlowsAndRejectsInvalidBalances()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var companyWalletId = $"company-wallet-{Guid.NewGuid():N}";
        var doctorWalletId = $"doctor-wallet-{Guid.NewGuid():N}";
        var platformWalletId = $"platform-wallet-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await unitOfWork.Wallets.AddWalletAsync(companyWalletId, WalletOwnerType.Company, ids.CompanyProfileId, ids.CompanyUserId);
        await unitOfWork.Wallets.AddWalletAsync(doctorWalletId, WalletOwnerType.Doctor, ids.DoctorProfileId, ids.DoctorUserId);
        await unitOfWork.Wallets.AddWalletAsync(platformWalletId, WalletOwnerType.Platform, "platform", null);
        await unitOfWork.SaveChangesAsync();

        await AddWalletMovementAsync(unitOfWork, companyWalletId, WalletTransactionType.TopUp, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, 100m, availableDelta: 100m);
        await AddWalletMovementAsync(unitOfWork, companyWalletId, WalletTransactionType.Reserve, WalletLedgerEntryDirection.Debit, WalletBalanceType.Available, 50m, availableDelta: -50m, reservedDelta: 50m);
        await AddWalletMovementAsync(unitOfWork, companyWalletId, WalletTransactionType.Charge, WalletLedgerEntryDirection.Debit, WalletBalanceType.Reserved, 50m, reservedDelta: -50m);
        await AddWalletMovementAsync(unitOfWork, doctorWalletId, WalletTransactionType.Earn, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, 40m, availableDelta: 40m);
        await AddWalletMovementAsync(unitOfWork, platformWalletId, WalletTransactionType.Earn, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, 10m, availableDelta: 10m);
        await AddWalletMovementAsync(unitOfWork, companyWalletId, WalletTransactionType.Refund, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, 50m, availableDelta: 50m);
        await AddWalletMovementAsync(unitOfWork, doctorWalletId, WalletTransactionType.WithdrawRequest, WalletLedgerEntryDirection.Debit, WalletBalanceType.Available, 25m, availableDelta: -25m, reservedDelta: 25m);
        await AddWalletMovementAsync(unitOfWork, doctorWalletId, WalletTransactionType.WithdrawApproved, WalletLedgerEntryDirection.Credit, WalletBalanceType.Reserved, 1m);
        await AddWalletMovementAsync(unitOfWork, doctorWalletId, WalletTransactionType.WithdrawRejected, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, 10m, availableDelta: 10m, reservedDelta: -10m);
        await AddWalletMovementAsync(unitOfWork, doctorWalletId, WalletTransactionType.WithdrawPayout, WalletLedgerEntryDirection.Debit, WalletBalanceType.Reserved, 15m, reservedDelta: -15m);

        Assert.Equal(100m, await Phase3DatabaseTestHelpers.GetAvailableBalanceAsync(factory.Services, companyWalletId));
        Assert.Equal(0m, await Phase3DatabaseTestHelpers.GetReservedBalanceAsync(factory.Services, companyWalletId));
        Assert.Equal(25m, await Phase3DatabaseTestHelpers.GetAvailableBalanceAsync(factory.Services, doctorWalletId));
        Assert.Equal(0m, await Phase3DatabaseTestHelpers.GetReservedBalanceAsync(factory.Services, doctorWalletId));
        Assert.Equal(10m, await Phase3DatabaseTestHelpers.GetAvailableBalanceAsync(factory.Services, platformWalletId));
        Assert.Equal(10, context.WalletLedgerEntries.Count());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => unitOfWork.Wallets.StageAvailableBalanceChangeAsync(doctorWalletId, -26m, DateTime.UtcNow));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => unitOfWork.WalletTransactions.AddTransactionAsync($"transaction-{Guid.NewGuid():N}", doctorWalletId, WalletTransactionType.Refund, $"invalid-{Guid.NewGuid():N}", 0m));
    }

    private static Task AddWalletMovementAsync(
        IDomainUnitOfWork unitOfWork,
        string walletId,
        WalletTransactionType operationType,
        WalletLedgerEntryDirection direction,
        WalletBalanceType balanceType,
        decimal amount,
        decimal availableDelta = 0m,
        decimal reservedDelta = 0m)
    {
        return unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            var transactionId = $"transaction-{Guid.NewGuid():N}";
            var updatedAtUtc = DateTime.UtcNow;
            if (availableDelta != 0m)
            {
                await unitOfWork.Wallets.StageAvailableBalanceChangeAsync(walletId, availableDelta, updatedAtUtc, cancellationToken);
            }

            if (reservedDelta != 0m)
            {
                await unitOfWork.Wallets.StageReservedBalanceChangeAsync(walletId, reservedDelta, updatedAtUtc, cancellationToken);
            }

            await unitOfWork.WalletTransactions.AddTransactionAsync(transactionId, walletId, operationType, $"{operationType}-{Guid.NewGuid():N}", amount, cancellationToken);
            await unitOfWork.WalletLedgerEntries.AddLedgerEntryAsync($"ledger-{Guid.NewGuid():N}", transactionId, walletId, direction, balanceType, amount, cancellationToken);
        });
    }
}
