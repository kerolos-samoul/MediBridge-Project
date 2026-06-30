using MediBridge.Core.Entities.Campaigns;
using MediBridge.Repository.Data;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class CampaignReviewMigrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public CampaignReviewMigrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Model_MapsCampaignModerationFoundation()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var campaign = context.Model.FindEntityType(typeof(Campaign));
        Assert.NotNull(campaign?.FindProperty(nameof(Campaign.SubmittedAtUtc)));
        Assert.Contains(
            campaign!.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Campaign.Status), nameof(Campaign.SubmittedAtUtc), nameof(Campaign.Id)]));

        var history = context.Model.FindEntityType(typeof(CampaignReviewHistory));
        Assert.NotNull(history?.FindProperty(nameof(CampaignReviewHistory.PriorStatus)));
        Assert.NotNull(history?.FindProperty(nameof(CampaignReviewHistory.ResultingStatus)));
        Assert.Equal(128, history?.FindProperty(nameof(CampaignReviewHistory.IdempotencyKey))?.GetMaxLength());

        var attempt = context.Model.FindEntityType(typeof(CampaignSubmissionAttempt));
        Assert.NotNull(attempt);
        Assert.Equal("CampaignSubmissionAttempts", attempt!.GetTableName());
        Assert.Equal(18, attempt.FindProperty(nameof(CampaignSubmissionAttempt.EstimatedCost))?.GetPrecision());
        Assert.Equal(2, attempt.FindProperty(nameof(CampaignSubmissionAttempt.EstimatedCost))?.GetScale());
        Assert.Equal(3, attempt.FindProperty(nameof(CampaignSubmissionAttempt.Currency))?.GetMaxLength());
        Assert.Equal(128, attempt.FindProperty(nameof(CampaignSubmissionAttempt.IdempotencyKey))?.GetMaxLength());
        Assert.Contains(
            attempt.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CampaignSubmissionAttempt.CampaignId), nameof(CampaignSubmissionAttempt.IdempotencyKey)]));
        Assert.Contains(
            attempt.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CampaignSubmissionAttempt.CampaignId), nameof(CampaignSubmissionAttempt.SubmittedAtUtc), nameof(CampaignSubmissionAttempt.Id)]));
    }

    [Fact]
    public void Migration_BackfillsExistingReviewHistoryWithValidLifecycleStates()
    {
        var migrationType = typeof(MediBridgeDbContext).Assembly
            .GetTypes()
            .Single(type => type.Name == "AddCampaignReviewModeration");
        var migration = Activator.CreateInstance(migrationType);
        var up = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        up!.Invoke(migration, [builder]);

        Assert.Contains(
            builder.Operations.OfType<SqlOperation>(),
            operation => operation.Sql.Contains("CampaignReviewHistories", StringComparison.Ordinal)
                && operation.Sql.Contains("PriorStatus", StringComparison.Ordinal)
                && operation.Sql.Contains("ResultingStatus", StringComparison.Ordinal));
    }
}
