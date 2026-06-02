using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3SoftDeleteQueryTests
{
    [Fact]
    public async Task SoftDeletedRecords_AreExcludedFromActiveQueries_ButRemainHistoricallyQueryable()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var campaignId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId);
        var walletId = $"wallet-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await unitOfWork.Wallets.AddWalletAsync(walletId, WalletOwnerType.Company, ids.CompanyProfileId, ids.CompanyUserId);
        await unitOfWork.AuditEvents.AddAuditEventAsync(
            $"audit-{Guid.NewGuid():N}",
            "campaign.soft-delete",
            AuditOutcome.Info,
            DateTime.UtcNow,
            """{"reason":"retained history"}""",
            correctsAuditEventId: null,
            targetType: AuditTargetType.Campaign,
            targetId: campaignId);
        await unitOfWork.SaveChangesAsync();

        var doctor = await context.DoctorProfiles.IgnoreQueryFilters().SingleAsync(profile => profile.Id == ids.DoctorProfileId);
        var company = await context.CompanyProfiles.IgnoreQueryFilters().SingleAsync(profile => profile.Id == ids.CompanyProfileId);
        var campaign = await context.Campaigns.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == campaignId);
        var wallet = await context.Wallets.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == walletId);

        doctor.IsDeleted = true;
        doctor.DeletedAtUtc = DateTime.UtcNow;
        company.IsDeleted = true;
        company.DeletedAtUtc = DateTime.UtcNow;
        campaign.IsDeleted = true;
        campaign.DeletedAtUtc = DateTime.UtcNow;
        wallet.IsDeleted = true;
        wallet.DeletedAtUtc = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync();

        Assert.Null(await context.DoctorProfiles.SingleOrDefaultAsync(profile => profile.Id == ids.DoctorProfileId));
        Assert.Null(await context.CompanyProfiles.SingleOrDefaultAsync(profile => profile.Id == ids.CompanyProfileId));
        Assert.Null(await unitOfWork.Campaigns.FindActiveCampaignIdAsync(campaignId));
        Assert.Null(await unitOfWork.Wallets.FindActiveWalletIdByOwnerAsync(WalletOwnerType.Company, ids.CompanyProfileId));

        Assert.Equal(ids.DoctorProfileId, await context.DoctorProfiles.IgnoreQueryFilters().Where(profile => profile.Id == ids.DoctorProfileId).Select(profile => profile.Id).SingleAsync());
        Assert.Equal(ids.CompanyProfileId, await context.CompanyProfiles.IgnoreQueryFilters().Where(profile => profile.Id == ids.CompanyProfileId).Select(profile => profile.Id).SingleAsync());
        Assert.Equal(campaignId, await unitOfWork.Campaigns.FindCampaignIdIncludingDeletedAsync(campaignId));
        Assert.Equal(walletId, await unitOfWork.Wallets.FindWalletIdByOwnerIncludingDeletedAsync(WalletOwnerType.Company, ids.CompanyProfileId));
        Assert.Contains(campaignId, await unitOfWork.AuditEvents.ListAuditEventTargetIdsIncludingHistoricalTargetsAsync(AuditTargetType.Campaign));
    }
}
