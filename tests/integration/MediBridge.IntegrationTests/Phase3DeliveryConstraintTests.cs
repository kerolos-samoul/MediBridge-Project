using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3DeliveryConstraintTests
{
    [Fact]
    public async Task AddDeliveryAsync_RejectsDuplicateDoctorDateCampaignDelivery()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var campaignId = await Phase3DatabaseTestHelpers.AddCampaignAsync(factory.Services, ids.CompanyProfileId);
        var deliveryDateEgypt = new DateOnly(2026, 6, 2);
        var deliveredAtUtc = new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc);

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await unitOfWork.Deliveries.AddDeliveryAsync("delivery-original", ids.DoctorProfileId, campaignId, ids.CompanyProfileId, deliveryDateEgypt, 50m, 10m, 5m, 45m, 50m, deliveredAtUtc);
        await unitOfWork.SaveChangesAsync();

        await unitOfWork.Deliveries.AddDeliveryAsync("delivery-duplicate", ids.DoctorProfileId, campaignId, ids.CompanyProfileId, deliveryDateEgypt, 50m, 10m, 5m, 45m, 50m, deliveredAtUtc);

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());
        Assert.True(await unitOfWork.Deliveries.DeliveryExistsAsync(ids.DoctorProfileId, deliveryDateEgypt, campaignId));
    }
}
