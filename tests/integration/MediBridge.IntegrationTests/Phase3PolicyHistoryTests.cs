using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3PolicyHistoryTests
{
    [Fact]
    public async Task PolicyCorrections_CreateLinkedRecords_WithoutChangingOriginals()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var now = DateTime.UtcNow;
        var originalPriceId = $"price-original-{Guid.NewGuid():N}";
        var correctedPriceId = $"price-correction-{Guid.NewGuid():N}";
        var originalFeeId = $"fee-original-{Guid.NewGuid():N}";
        var correctedFeeId = $"fee-correction-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await unitOfWork.PolicyHistory.AddDoctorPriceHistoryAsync(originalPriceId, ids.DoctorProfileId, 40m, 50m, ids.AdminUserId);
        await unitOfWork.PolicyHistory.AddPlatformFeePolicyHistoryAsync(originalFeeId, 10m, now, ids.AdminUserId);
        await unitOfWork.SaveChangesAsync();

        await unitOfWork.PolicyHistory.AddDoctorPriceHistoryCorrectionAsync(correctedPriceId, originalPriceId, ids.DoctorProfileId, 40m, 55m, ids.AdminUserId);
        await unitOfWork.PolicyHistory.AddPlatformFeePolicyHistoryCorrectionAsync(correctedFeeId, originalFeeId, 12m, now.AddMinutes(1), ids.AdminUserId);
        await unitOfWork.SaveChangesAsync();

        var originalPrice = await context.DoctorPriceHistories.SingleAsync(history => history.Id == originalPriceId);
        var correctedPrice = await context.DoctorPriceHistories.SingleAsync(history => history.Id == correctedPriceId);
        var originalFee = await context.PlatformFeePolicyHistories.SingleAsync(history => history.Id == originalFeeId);
        var correctedFee = await context.PlatformFeePolicyHistories.SingleAsync(history => history.Id == correctedFeeId);

        Assert.Equal(50m, originalPrice.NewPricePerMessage);
        Assert.Null(originalPrice.CorrectsHistoryId);
        Assert.Equal(originalPriceId, correctedPrice.CorrectsHistoryId);
        Assert.Equal(55m, correctedPrice.NewPricePerMessage);
        Assert.Equal(10m, originalFee.FeePercent);
        Assert.Null(originalFee.CorrectsHistoryId);
        Assert.Equal(originalFeeId, correctedFee.CorrectsHistoryId);
        Assert.Equal(12m, correctedFee.FeePercent);
    }
}
