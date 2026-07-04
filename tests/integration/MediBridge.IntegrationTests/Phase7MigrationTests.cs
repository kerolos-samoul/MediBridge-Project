using System.Data;
using System.Reflection;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7MigrationTests
{
    private const string PreviousMigration = "20260701172632_AddMockPaymentTransactionsClean";

    [Fact]
    public void Phase7Migration_ContainsAuthenticQueuedBackfillAndAbortGuard()
    {
        var migrationType = typeof(MediBridgeDbContext).Assembly.GetTypes()
            .Single(type => type.Name == "AddPhase7DeliveryExpiryJobs");
        var migration = Activator.CreateInstance(migrationType);
        var up = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        up.Invoke(migration, [builder]);

        var sql = string.Join(Environment.NewLine, builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        Assert.Contains("campaigns.[SubmittedAtUtc]", sql, StringComparison.Ordinal);
        Assert.Contains("queueRows.[Status] = 1", sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51007", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("QueuedAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains(builder.Operations.OfType<CreateTableOperation>(), operation => operation.Name == "DeliveryJobRuns");
        Assert.Contains(builder.Operations.OfType<CreateTableOperation>(), operation => operation.Name == "DeliveryRecoveryDispatches");
    }

    [Fact]
    public async Task Phase7Migration_BackfillsOnlyQueuedRows_PreservesTerminalNulls_AndReappliesWithoutBusinessMutation()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.EnsureDeletedAsync();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var authenticSubmittedAtUtc = new DateTime(2026, 6, 20, 9, 15, 0, DateTimeKind.Utc);
        await SeedLegacyQueueRowsAsync(context, authenticSubmittedAtUtc, unresolvedQueued: false);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var rows = await context.DoctorMessageQueues.AsNoTracking().OrderBy(queue => queue.Id).ToListAsync();
        Assert.Equal(authenticSubmittedAtUtc, rows.Single(queue => queue.Id == "queue-queued").CampaignSubmittedAtUtc);
        Assert.Null(rows.Single(queue => queue.Id == "queue-activated").CampaignSubmittedAtUtc);
        Assert.Null(rows.Single(queue => queue.Id == "queue-cancelled").CampaignSubmittedAtUtc);
        await AssertSchemaAsync(context);
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE [DoctorMessageQueues] SET [Status] = 1 WHERE [Id] = 'queue-activated'"));
        Assert.Equal((int)QueueItemStatus.Activated, await ExecuteScalarIntAsync(
            context,
            "SELECT [Status] FROM [DoctorMessageQueues] WHERE [Id] = 'queue-activated'"));
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO [DeliveryRecoveryDispatches] ([Id], [BusinessDateEgypt], [JobType], [Status], [ClaimedAtUtc])
            VALUES ('dispatch-1', '2026-07-02', 1, 1, '2026-07-02T00:00:00Z')
            """);
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO [DeliveryRecoveryDispatches] ([Id], [BusinessDateEgypt], [JobType], [Status], [ClaimedAtUtc])
            VALUES ('dispatch-2', '2026-07-02', 1, 1, '2026-07-02T00:01:00Z')
            """));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(3, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM [DoctorMessageQueues] WHERE [Id] LIKE 'queue-%'"));

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(3, await context.DoctorMessageQueues.AsNoTracking().CountAsync(queue => queue.Id.StartsWith("queue-")));
        Assert.Null((await context.DoctorMessageQueues.AsNoTracking().SingleAsync(queue => queue.Id == "queue-activated")).CampaignSubmittedAtUtc);
    }

    [Fact]
    public async Task Phase7Migration_RejectsUnresolvableQueuedRowsInsteadOfUsingQueuedAtUtc()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.EnsureDeletedAsync();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await SeedLegacyQueueRowsAsync(context, authenticSubmittedAtUtc: null, unresolvedQueued: true);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());

        Assert.Contains("authentic campaign submission timestamp", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedLegacyQueueRowsAsync(MediBridgeDbContext context, DateTime? authenticSubmittedAtUtc, bool unresolvedQueued)
    {
        var createdAtUtc = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        context.Users.AddRange(
            CreateUser("phase7-migration-doctor-user", "phase7-migration-doctor@example.com", UserRole.Doctor),
            CreateUser("phase7-migration-company-user", "phase7-migration-company@example.com", UserRole.Company));
        context.DoctorProfiles.Add(new DoctorProfile
        {
            Id = "phase7-migration-doctor",
            UserId = "phase7-migration-doctor-user",
            Specialization = "Cardiology",
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "migration/doctor",
            DailyMessageLimit = 10,
            PricePerMessage = 50m,
            CreatedAtUtc = createdAtUtc
        });
        context.CompanyProfiles.Add(new CompanyProfile
        {
            Id = "phase7-migration-company",
            UserId = "phase7-migration-company-user",
            CompanyName = "Migration Company",
            LicenseNumber = $"migration-license-{Guid.NewGuid():N}",
            ContactName = "Migration Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "migration/company",
            CreatedAtUtc = createdAtUtc
        });

        var queuedCampaign = CreateCampaign("phase7-migration-campaign-queued", authenticSubmittedAtUtc, createdAtUtc);
        var activatedCampaign = CreateCampaign("phase7-migration-campaign-activated", null, createdAtUtc);
        var cancelledCampaign = CreateCampaign("phase7-migration-campaign-cancelled", null, createdAtUtc);
        context.Campaigns.AddRange(queuedCampaign, activatedCampaign, cancelledCampaign);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await InsertLegacyQueueAsync(context, "queue-queued", queuedCampaign.Id, QueueItemStatus.Queued, createdAtUtc.AddHours(5));
        await InsertLegacyQueueAsync(context, "queue-activated", activatedCampaign.Id, QueueItemStatus.Activated, createdAtUtc.AddHours(1));
        await InsertLegacyQueueAsync(context, "queue-cancelled", cancelledCampaign.Id, QueueItemStatus.Cancelled, createdAtUtc.AddHours(2));
    }

    private static MediBridgeIdentityUser CreateUser(string id, string email, UserRole role) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        Role = role,
        AccountStatus = AccountStatus.Approved,
        EmailVerified = true,
        EmailConfirmed = true,
        CreatedAtUtc = new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Utc)
    };

    private static Campaign CreateCampaign(string id, DateTime? submittedAtUtc, DateTime createdAtUtc) => new()
    {
        Id = id,
        CompanyId = "phase7-migration-company",
        Title = id,
        Description = "Phase 7 migration fixture",
        Status = CampaignStatus.Approved,
        SubmittedAtUtc = submittedAtUtc,
        CreatedAtUtc = createdAtUtc
    };

    private static Task<int> InsertLegacyQueueAsync(
        MediBridgeDbContext context,
        string id,
        string campaignId,
        QueueItemStatus status,
        DateTime queuedAtUtc)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [DoctorMessageQueues]
                ([Id], [DoctorId], [CampaignId], [QueuedAtUtc], [CampaignSubmittedAtUtc], [Status], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                ({id}, {"phase7-migration-doctor"}, {campaignId}, {queuedAtUtc}, {null}, {(int)status}, {queuedAtUtc}, {null})
            """);
    }

    private static async Task AssertSchemaAsync(MediBridgeDbContext context)
    {
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeliveryJobRuns'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeliveryRecoveryDispatches'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.check_constraints WHERE [name] = 'CK_DoctorMessageQueues_QueuedCampaignSubmittedAtUtc'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DeliveryRecoveryDispatches_BusinessDateEgypt_JobType' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_CampaignId' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_WalletTransactions_OperationType_IdempotencyKey' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('DoctorMessageQueues') AND [name] = 'ConcurrencyToken' AND [system_type_id] = 189"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('DoctorAdDeliveries') AND [name] = 'ExpiredAtUtc'"));
    }

    private static async Task<int> ExecuteScalarIntAsync(MediBridgeDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
