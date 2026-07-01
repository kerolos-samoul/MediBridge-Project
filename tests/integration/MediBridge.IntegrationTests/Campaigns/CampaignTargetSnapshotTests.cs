using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CampaignTargetSnapshotTests
{
    [Fact]
    public async Task CampaignSubmission_SnapshotsDoctorPriceAtSubmission()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, ids.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);

        using var firstPrice = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{ids.DoctorProfileId}/price",
            new { PricePerMessage = 40m, Reason = "Submission price" });
        Assert.Equal(HttpStatusCode.OK, firstPrice.StatusCode);

        string campaignId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var domainUnitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
            var doctor = await context.DoctorProfiles.AsNoTracking().SingleAsync(profile => profile.Id == ids.DoctorProfileId);
            var campaign = new Campaign
            {
                CompanyId = ids.CompanyProfileId,
                Title = "Snapshot campaign",
                Description = "Submission captures doctor facts"
            };
            campaignId = campaign.Id;
            await domainUnitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
            {
                await domainUnitOfWork.Campaigns.AddCampaignAsync(campaign, cancellationToken);
                await domainUnitOfWork.Campaigns.AddCampaignTargetAsync(
                    CampaignTargetEligibility.CreateSnapshot(campaign.Id, doctor),
                    cancellationToken);
            });
        }

        using var secondPrice = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{ids.DoctorProfileId}/price",
            new { PricePerMessage = 75m, Reason = "Later price" });
        Assert.Equal(HttpStatusCode.OK, secondPrice.StatusCode);

        using var assertScope = factory.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var target = await assertContext.CampaignTargets.AsNoTracking().SingleAsync(item => item.CampaignId == campaignId);
        Assert.Equal(40m, target.PricePerMessageSnapshot);
        Assert.Equal("Cardiology", target.SpecializationSnapshot);
        Assert.Equal(8, target.ExperienceYearsSnapshot);
        Assert.Equal("Cairo", target.LocationSnapshot);
        Assert.Equal(95m, target.ActivityScoreSnapshot);
    }
}
