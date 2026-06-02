using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3AuditEventTests
{
    [Fact]
    public async Task AuditEventCorrection_CreatesLinkedRecord_WithSafeMetadata()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var originalId = $"audit-original-{Guid.NewGuid():N}";
        var correctionId = $"audit-correction-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await unitOfWork.AuditEvents.AddAuditEventAsync(originalId, "campaign.review", AuditOutcome.Info, DateTime.UtcNow, """{"source":"admin"}""");
        await unitOfWork.SaveChangesAsync();

        await unitOfWork.AuditEvents.AddAuditEventAsync(
            correctionId,
            "campaign.review.corrected",
            AuditOutcome.Info,
            DateTime.UtcNow,
            """{"source":"admin","correction":"reason"}""",
            originalId);
        await unitOfWork.SaveChangesAsync();

        var original = await context.AuditEvents.SingleAsync(auditEvent => auditEvent.Id == originalId);
        var correction = await context.AuditEvents.SingleAsync(auditEvent => auditEvent.Id == correctionId);

        Assert.Null(original.CorrectsAuditEventId);
        Assert.Equal(originalId, correction.CorrectsAuditEventId);
        Assert.Contains("correction", correction.Metadata, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"password":"secret"}""")]
    [InlineData("""{"plainTextToken":"token"}""")]
    [InlineData("""{"requestBody":{"field":"value"}}""")]
    [InlineData("""{"responseBody":{"field":"value"}}""")]
    public async Task AuditEventMetadata_RejectsSensitivePayloads(string unsafeMetadata)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await Assert.ThrowsAsync<ArgumentException>(() => unitOfWork.AuditEvents.AddAuditEventAsync(
            $"audit-{Guid.NewGuid():N}",
            "auth.sensitive",
            AuditOutcome.Denied,
            DateTime.UtcNow,
            unsafeMetadata));
    }
}
