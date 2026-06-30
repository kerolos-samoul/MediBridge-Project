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

    [Fact]
    public void Migration_GuardsReviewIdempotencyArtifactsFromEarlierMigrationLineage()
    {
        var migrationType = typeof(MediBridgeDbContext).Assembly
            .GetTypes()
            .Single(type => type.Name == "AddCampaignReviewModeration");
        var migration = Activator.CreateInstance(migrationType);
        var up = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        up!.Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        Assert.Contains("COL_LENGTH('dbo.CampaignReviewHistories', 'IdempotencyKey')", sql, StringComparison.Ordinal);
        Assert.Contains("IX_CampaignReviewHistories_CampaignId_IdempotencyKey", sql, StringComparison.Ordinal);
        Assert.Contains("sys.indexes", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            builder.Operations.OfType<AddColumnOperation>(),
            operation => operation.Table == "CampaignReviewHistories" && operation.Name == "IdempotencyKey");
        Assert.DoesNotContain(
            builder.Operations.OfType<CreateIndexOperation>(),
            operation => operation.Table == "CampaignReviewHistories" && operation.Name == "IX_CampaignReviewHistories_CampaignId_IdempotencyKey");
    }

    [Fact]
    public void Phase5Migration_PreservesQueueIndexesFromDownstreamMigrationLineage()
    {
        var migrationType = typeof(MediBridgeDbContext).Assembly
            .GetTypes()
            .Single(type => type.Name == "Phase5CampaignQueueFoundation");
        var migration = Activator.CreateInstance(migrationType);
        var up = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        up!.Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        Assert.Contains("IX_DoctorMessageQueues_CampaignId", sql, StringComparison.Ordinal);
        Assert.Contains("IX_DoctorMessageQueues_CampaignId_DoctorId", sql, StringComparison.Ordinal);
        Assert.Contains("sys.indexes", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            builder.Operations.OfType<DropIndexOperation>(),
            operation => operation.Table == "DoctorMessageQueues");
        Assert.DoesNotContain(
            builder.Operations.OfType<CreateIndexOperation>(),
            operation => operation.Table == "DoctorMessageQueues");
        Assert.Contains(
            builder.Operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "CampaignSubmissionRequests");
    }

    [Fact]
    public void StorageReconciliationMigration_ConvertsKnownResourceTypesToIntegratedEnumValues()
    {
        var migrationType = typeof(MediBridgeDbContext).Assembly
            .GetTypes()
            .SingleOrDefault(type => type.Name == "ReconcileStoredFileResourceType");

        Assert.NotNull(migrationType);
        var migration = Activator.CreateInstance(migrationType!);
        var up = migrationType!.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        up!.Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        var normalizedSql = sql.Replace("''", "'", StringComparison.Ordinal);

        Assert.Contains("StorageResourceType", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN N'image' THEN 1", normalizedSql, StringComparison.Ordinal);
        Assert.Contains("WHEN N'video' THEN 2", normalizedSql, StringComparison.Ordinal);
        Assert.Contains("WHEN N'raw' THEN 3", normalizedSql, StringComparison.Ordinal);
        Assert.Contains("THROW", sql, StringComparison.Ordinal);
    }
}
