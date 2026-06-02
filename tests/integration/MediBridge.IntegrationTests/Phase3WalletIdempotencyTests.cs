using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3WalletIdempotencyTests
{
    public static TheoryData<WalletTransactionType> RetriableOperationTypes => new()
    {
        WalletTransactionType.TopUp,
        WalletTransactionType.Charge,
        WalletTransactionType.Earn,
        WalletTransactionType.Refund,
        WalletTransactionType.WithdrawPayout
    };

    [Theory]
    [MemberData(nameof(RetriableOperationTypes))]
    public async Task AddTransactionAsync_RejectsDuplicateOperationTypeAndIdempotencyKeyWithoutDoubleApplyingBalance(WalletTransactionType operationType)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var walletId = $"wallet-{operationType}-{Guid.NewGuid():N}";
        var idempotencyKey = $"retry-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await unitOfWork.Wallets.AddWalletAsync(walletId, WalletOwnerType.Company, $"{ids.CompanyProfileId}-{operationType}", ids.CompanyUserId);
        await unitOfWork.SaveChangesAsync();

        await unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            await unitOfWork.Wallets.StageAvailableBalanceChangeAsync(walletId, 10m, cancellationToken);
            await unitOfWork.WalletTransactions.AddTransactionAsync($"transaction-{Guid.NewGuid():N}", walletId, operationType, idempotencyKey, 10m, cancellationToken);
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            await unitOfWork.Wallets.StageAvailableBalanceChangeAsync(walletId, 10m, cancellationToken);
            await unitOfWork.WalletTransactions.AddTransactionAsync($"transaction-{Guid.NewGuid():N}", walletId, operationType, idempotencyKey, 10m, cancellationToken);
        }));

        Assert.Equal(10m, await Phase3DatabaseTestHelpers.GetAvailableBalanceAsync(factory.Services, walletId));
        Assert.True(await unitOfWork.WalletTransactions.IdempotencyKeyExistsAsync(operationType, idempotencyKey));
    }
}
