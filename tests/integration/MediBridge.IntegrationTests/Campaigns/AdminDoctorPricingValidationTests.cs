using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminDoctorPricingValidationTests
{
    [Fact]
    public async Task AdminDoctorPricing_InvalidValuesAreRejectedAndDoNotChangeExistingSnapshots()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, ids.AdminUserId);

        using var validResponse = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{ids.DoctorProfileId}/price",
            new { PricePerMessage = 65m, Reason = "Valid baseline" });
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        var campaignId = Guid.NewGuid().ToString("N");
        var targetId = Guid.NewGuid().ToString("N");
        using (var seedScope = factory.Services.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            await seedContext.Campaigns.AddAsync(new Campaign
            {
                Id = campaignId,
                CompanyId = ids.CompanyProfileId,
                Title = "Submitted snapshot",
                Description = "Immutable price test",
                Status = CampaignStatus.PendingReview
            });
            await seedContext.CampaignTargets.AddAsync(new CampaignTarget
            {
                Id = targetId,
                CampaignId = campaignId,
                DoctorId = ids.DoctorProfileId,
                SpecializationSnapshot = "Cardiology",
                ExperienceYearsSnapshot = 8,
                LocationSnapshot = "Cairo",
                ActivityScoreSnapshot = 95m,
                PricePerMessageSnapshot = 42m
            });
            await seedContext.SaveChangesAsync();
        }

        object?[] invalidPrices = [null, 0m, -1m, 1.001m];
        foreach (var invalidPrice in invalidPrices)
        {
            using var response = await admin.PutAsJsonAsync(
                $"/api/admin/doctors/{ids.DoctorProfileId}/price",
                new { PricePerMessage = invalidPrice, Reason = "Must be rejected" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var assertScope = factory.Services.CreateScope();
        var context = assertScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var persistedDoctor = await context.DoctorProfiles.AsNoTracking().SingleAsync(profile => profile.Id == ids.DoctorProfileId);
        var persistedTarget = await context.CampaignTargets.AsNoTracking().SingleAsync(target => target.Id == targetId);
        Assert.Equal(65m, persistedDoctor.PricePerMessage);
        Assert.Equal(42m, persistedTarget.PricePerMessageSnapshot);
        Assert.Equal(1, await context.DoctorPriceHistories.CountAsync(history => history.DoctorId == ids.DoctorProfileId));
    }
}
