using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7InjectorIntegrityTests
{
    [Fact]
    public async Task DeliveryRepository_RejectsNonUtcActivationTimestamp()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync("local-delivery-time", scenario.UtcNow.AddDays(-1), scenario.UtcNow);
        using var scope = scenario.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var localTimestamp = DateTime.SpecifyKind(scenario.UtcNow, DateTimeKind.Local);

        await Assert.ThrowsAsync<ArgumentException>(() => unitOfWork.ExecuteIsolatedInTransactionAsync(async cancellationToken =>
        {
            await unitOfWork.Deliveries.AddDeliveryAsync(
                $"delivery-local-time-{Guid.NewGuid():N}",
                scenario.DoctorId,
                candidate.CampaignId,
                candidate.CompanyId,
                scenario.BusinessDateEgypt,
                50m,
                20m,
                10m,
                40m,
                50m,
                localTimestamp,
                cancellationToken);
            return true;
        }));
    }

    [Fact]
    public async Task WalletRepository_RejectsNonUtcMutationTimestamp()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync("local-wallet-time", scenario.UtcNow.AddDays(-1), scenario.UtcNow);
        using var scope = scenario.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var localTimestamp = DateTime.SpecifyKind(scenario.UtcNow, DateTimeKind.Local);

        await Assert.ThrowsAsync<ArgumentException>(() => unitOfWork.ExecuteIsolatedInTransactionAsync(async cancellationToken =>
        {
            Assert.NotNull(await unitOfWork.Wallets.FindActiveWalletForUpdateAsync(candidate.WalletId, cancellationToken));
            await unitOfWork.Wallets.StageAvailableBalanceChangeAsync(candidate.WalletId, -1m, localTimestamp, cancellationToken);
            return true;
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => unitOfWork.ExecuteIsolatedInTransactionAsync(async cancellationToken =>
        {
            await unitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
                $"wallet-local-create-{Guid.NewGuid():N}",
                MediBridge.Core.Enums.WalletOwnerType.Company,
                candidate.CompanyId,
                candidate.CompanyUserId,
                localTimestamp,
                cancellationToken);
            return true;
        }));
    }


    [Fact]
    public async Task DoctorEligibilityRead_LocksTheIdentityUserForTheWholeTransaction()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        using var lockingScope = scenario.Services.CreateScope();
        var lockingContext = lockingScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var repository = lockingScope.ServiceProvider.GetRequiredService<IProfileRepository>();
        await using var transaction = await lockingContext.Database.BeginTransactionAsync();

        Assert.NotNull(await repository.FindDoctorDeliveryEligibilityForUpdateAsync(scenario.DoctorId));

        var exception = await AssertBlockedUserUpdateAsync(scenario.Services, scenario.DoctorUserId);
        Assert.Equal(1222, exception.Number);
    }

    [Fact]
    public async Task CampaignEligibilityRead_LocksCompanyProfileAndIdentityUserForTheWholeTransaction()
    {
        await using var scenario = await Phase7InjectorTestHarness.CreateAsync();
        var candidate = await scenario.AddCandidateAsync("eligibility-lock", scenario.UtcNow.AddDays(-1), scenario.UtcNow);
        using var lockingScope = scenario.Services.CreateScope();
        var lockingContext = lockingScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var repository = lockingScope.ServiceProvider.GetRequiredService<ICampaignRepository>();
        await using var transaction = await lockingContext.Database.BeginTransactionAsync();

        Assert.NotNull(await repository.FindDeliveryEligibilityForUpdateAsync(candidate.CampaignId));

        var profileException = await AssertBlockedCompanyProfileUpdateAsync(scenario.Services, candidate.CompanyId);
        Assert.Equal(1222, profileException.Number);
        var userException = await AssertBlockedUserUpdateAsync(scenario.Services, candidate.CompanyUserId);
        Assert.Equal(1222, userException.Number);
    }

    private static async Task<SqlException> AssertBlockedUserUpdateAsync(IServiceProvider services, string userId)
    {
        using var competingScope = services.CreateScope();
        var competingContext = competingScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await Assert.ThrowsAsync<SqlException>(() =>
            competingContext.Database.ExecuteSqlInterpolatedAsync(
                $"SET LOCK_TIMEOUT 250; UPDATE [Users] SET [AccountStatus] = [AccountStatus] WHERE [Id] = {userId}"));
    }

    private static async Task<SqlException> AssertBlockedCompanyProfileUpdateAsync(IServiceProvider services, string companyId)
    {
        using var competingScope = services.CreateScope();
        var competingContext = competingScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await Assert.ThrowsAsync<SqlException>(() =>
            competingContext.Database.ExecuteSqlInterpolatedAsync(
                $"SET LOCK_TIMEOUT 250; UPDATE [CompanyProfiles] SET [IsDeleted] = [IsDeleted] WHERE [Id] = {companyId}"));
    }
}
