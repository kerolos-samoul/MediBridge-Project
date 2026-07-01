using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Wallets;

public sealed class IdentityTransactionConflictMappingTests
{
    [Fact]
    public async Task IdentityTransaction_NonIdentityUniqueViolationIsNotMisclassifiedAsRegistrationConflict()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);

        using var scope = factory.Services.CreateScope();
        var identityUnitOfWork = scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>();
        var domainUnitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await domainUnitOfWork.ExecuteInTransactionAsync(cancellationToken =>
            domainUnitOfWork.Wallets.AddWalletAsync(
                Guid.NewGuid().ToString("N"),
                WalletOwnerType.Company,
                ids.CompanyProfileId,
                ids.CompanyUserId,
                cancellationToken));

        var exception = await Record.ExceptionAsync(() => identityUnitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            var companyUser = await identityUnitOfWork.Users.FindByIdAsync(ids.CompanyUserId, cancellationToken);
            Assert.NotNull(companyUser);
            companyUser.LastStatusChangedAtUtc = DateTime.UtcNow.AddMinutes(1);
            await identityUnitOfWork.Users.UpdateAsync(companyUser, cancellationToken);
            await domainUnitOfWork.Wallets.AddWalletAsync(
                Guid.NewGuid().ToString("N"),
                WalletOwnerType.Company,
                ids.CompanyProfileId,
                ids.CompanyUserId,
                cancellationToken);
        }));

        Assert.IsType<DbUpdateException>(exception);
        Assert.IsNotType<IdentityRecordConflictException>(exception);
    }
}
