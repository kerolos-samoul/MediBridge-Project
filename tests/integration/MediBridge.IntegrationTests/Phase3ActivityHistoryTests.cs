using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3ActivityHistoryTests
{
    [Fact]
    public async Task ActivityScoreCorrection_CreatesLinkedRecord_WithoutChangingOriginal()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await Phase3DatabaseTestHelpers.SeedProfilesAsync(factory.Services);
        var originalId = $"activity-original-{Guid.NewGuid():N}";
        var correctionId = $"activity-correction-{Guid.NewGuid():N}";
        var windowStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var windowEnd = DateOnly.FromDateTime(DateTime.UtcNow);

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await unitOfWork.PolicyHistory.AddActivityScoreHistoryAsync(originalId, ids.DoctorProfileId, 80m, windowStart, windowEnd);
        await unitOfWork.SaveChangesAsync();

        await unitOfWork.PolicyHistory.AddActivityScoreHistoryCorrectionAsync(correctionId, originalId, ids.DoctorProfileId, 90m, windowStart, windowEnd);
        await unitOfWork.SaveChangesAsync();

        var original = await context.ActivityScoreHistories.SingleAsync(history => history.Id == originalId);
        var correction = await context.ActivityScoreHistories.SingleAsync(history => history.Id == correctionId);

        Assert.Equal(80m, original.ActivityScore);
        Assert.Null(original.CorrectsHistoryId);
        Assert.Equal(originalId, correction.CorrectsHistoryId);
        Assert.Equal(90m, correction.ActivityScore);
    }
}
