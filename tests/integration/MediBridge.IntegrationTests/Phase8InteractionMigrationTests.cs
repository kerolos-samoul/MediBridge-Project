using System.Reflection;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Testcontainers.MsSql;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionMigrationTests
{
    private const string PreviousMigration = "20260702174103_AddPhase7DeliveryExpiryJobs";
    private const string Phase8Migration = "20260711130805_AddPhase8InteractionPayments";

    [Fact]
    public void Phase8Migration_CreatesInteractionEvidenceWithoutBusinessDataRewrite()
    {
        var migrationType = typeof(MediBridge.Repository.Data.MediBridgeDbContext).Assembly.GetTypes()
            .Single(type => type.Name == "AddPhase8InteractionPayments");
        var migration = Activator.CreateInstance(migrationType);
        var up = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        up.Invoke(migration, [builder]);

        Assert.Empty(builder.Operations.OfType<SqlOperation>());
        var alterPercent = Assert.Single(builder.Operations.OfType<AlterColumnOperation>(), operation =>
            operation.Table == "DoctorAdDeliveries" && operation.Name == "PlatformFeePercentSnapshot");
        Assert.Equal(7, alterPercent.Precision);
        Assert.Equal(3, alterPercent.Scale);

        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>(), operation => operation.Name == "DeliveryInteractions");
        Assert.Contains(table.Columns, column => column.Name == "IdempotencyKeyHash" && column.MaxLength == 128);
        Assert.Contains(table.Columns, column => column.Name == "RequestFingerprint" && column.MaxLength == 128);
        Assert.Contains(table.Columns, column => column.Name == "FeedbackText" && column.MaxLength == 2000);
        Assert.Contains(table.Columns, column => column.Name == "ConcurrencyToken" && column.IsRowVersion);
        Assert.Contains(table.CheckConstraints, check => check.Name == "CK_DeliveryInteractions_Outcome");
        Assert.Contains(table.CheckConstraints, check => check.Name == "CK_DeliveryInteractions_Feedback_Length");
        Assert.All(table.ForeignKeys, foreignKey => Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete));

        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, index => index.Table == "DeliveryInteractions" && index.Name == "IX_DeliveryInteractions_DeliveryId" && index.IsUnique);
        Assert.Contains(indexes, index => index.Table == "DeliveryInteractions" && index.Name == "IX_DeliveryInteractions_DoctorId_IdempotencyKeyHash" && index.IsUnique);
        Assert.Contains(indexes, index => index.Table == "DoctorAdDeliveries" && index.Name == "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Status_ReservationStatus_Id");
    }

    [Phase8SqlServerMigrationFact]
    public async Task Phase8Migration_AppliesConstraintsAndCanRollbackReapplyWithoutBusinessMutation()
    {
        await using var sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await sql.StartAsync();

        var options = new DbContextOptionsBuilder<MediBridgeDbContext>()
            .UseSqlServer(sql.GetConnectionString())
            .Options;
        await using var context = new MediBridgeDbContext(options);
        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        await SeedPhase7BusinessRowsAsync(context);
        var before = await CountBusinessRowsAsync(context);

        await migrator.MigrateAsync(Phase8Migration);

        await AssertPhase8SchemaAsync(context);
        await InsertInteractionAsync(context, "interaction-1", "delivery-phase8-migration", "idem-hash-1", "fingerprint-1", feedbackLength: 2000);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            InsertInteractionAsync(context, "interaction-duplicate-delivery", "delivery-phase8-migration", "idem-hash-2", "fingerprint-2", feedbackLength: 10));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            InsertInteractionAsync(context, "interaction-duplicate-idem", "delivery-phase8-migration-2", "idem-hash-1", "fingerprint-3", feedbackLength: 10));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            InsertInteractionAsync(context, "interaction-long-feedback", "delivery-phase8-migration-2", "idem-hash-3", "fingerprint-4", feedbackLength: 2001));

        Assert.Equal(before, await CountBusinessRowsAsync(context));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM [DeliveryInteractions]"));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(before, await CountBusinessRowsAsync(context));
        Assert.Equal(0, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeliveryInteractions'"));

        await migrator.MigrateAsync(Phase8Migration);
        Assert.Equal(before, await CountBusinessRowsAsync(context));
        await AssertPhase8SchemaAsync(context);
    }

    private static async Task SeedPhase7BusinessRowsAsync(MediBridgeDbContext context)
    {
        var now = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        context.Users.AddRange(
            CreateUser("phase8-migration-doctor-user", "phase8-migration-doctor@example.test", UserRole.Doctor),
            CreateUser("phase8-migration-company-user", "phase8-migration-company@example.test", UserRole.Company));
        context.DoctorProfiles.Add(new DoctorProfile
        {
            Id = "doctor-phase8-migration",
            UserId = "phase8-migration-doctor-user",
            Specialization = "Cardiology",
            ExperienceYears = 10,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "migration/doctor",
            DailyMessageLimit = 10,
            PricePerMessage = 100m,
            Status = DoctorMarketplaceStatus.Active,
            CreatedAtUtc = now
        });
        context.CompanyProfiles.Add(new CompanyProfile
        {
            Id = "company-phase8-migration",
            UserId = "phase8-migration-company-user",
            CompanyName = "Phase 8 Migration Company",
            LicenseNumber = $"license-{Guid.NewGuid():N}",
            ContactName = "Migration Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "migration/company",
            CreatedAtUtc = now
        });
        context.Campaigns.AddRange(
            CreateCampaign("campaign-phase8-migration", now),
            CreateCampaign("campaign-phase8-migration-2", now));
        context.Wallets.AddRange(
            new Wallet
            {
                Id = "company-wallet-phase8-migration",
                OwnerType = WalletOwnerType.Company,
                OwnerId = "company-phase8-migration",
                OwnerUserId = "phase8-migration-company-user",
                AvailableBalance = 0m,
                ReservedBalance = 100m,
                Currency = "EGP",
                CreatedAtUtc = now
            },
            new Wallet
            {
                Id = "doctor-wallet-phase8-migration",
                OwnerType = WalletOwnerType.Doctor,
                OwnerId = "doctor-phase8-migration",
                OwnerUserId = "phase8-migration-doctor-user",
                AvailableBalance = 0m,
                ReservedBalance = 0m,
                Currency = "EGP",
                CreatedAtUtc = now
            });
        context.DoctorAdDeliveries.AddRange(
            CreateDelivery("delivery-phase8-migration", "campaign-phase8-migration", now),
            CreateDelivery("delivery-phase8-migration-2", "campaign-phase8-migration-2", now));
        context.WalletTransactions.AddRange(
            CreateTransaction("charge-transaction-phase8-migration", "company-wallet-phase8-migration", WalletTransactionType.Charge, "delivery:charge:delivery-phase8-migration", "delivery-phase8-migration", 100m, now),
            CreateTransaction("earn-transaction-phase8-migration", "doctor-wallet-phase8-migration", WalletTransactionType.Earn, "delivery:earn:delivery-phase8-migration", "delivery-phase8-migration", 87.65m, now));
        context.AuditEvents.Add(new AuditEvent
        {
            Id = "audit-phase8-migration",
            EventType = "Phase8InteractionSettlementSucceeded",
            TargetType = AuditTargetType.Delivery,
            TargetId = "delivery-phase8-migration",
            Outcome = AuditOutcome.Success,
            CreatedAtUtc = now
        });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
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
        CreatedAtUtc = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc)
    };

    private static Campaign CreateCampaign(string id, DateTime now) => new()
    {
        Id = id,
        CompanyId = "company-phase8-migration",
        Title = id,
        Description = "Phase 8 migration fixture",
        Status = CampaignStatus.Approved,
        SubmittedAtUtc = now.AddDays(-1),
        CreatedAtUtc = now
    };

    private static DoctorAdDelivery CreateDelivery(string id, string campaignId, DateTime now) => new()
    {
        Id = id,
        DoctorId = "doctor-phase8-migration",
        CampaignId = campaignId,
        CompanyId = "company-phase8-migration",
        DeliveryDateEgypt = new DateOnly(2026, 7, 11),
        DeliveredAtUtc = now,
        Status = DeliveryStatus.Active,
        ReservationStatus = ReservationStatus.Reserved,
        PricePerMessageSnapshot = 100m,
        PlatformFeePercentSnapshot = 12.345m,
        PlatformFeeAmount = 12.35m,
        DoctorEarnings = 87.65m,
        ReservedAmount = 100m,
        CreatedAtUtc = now
    };

    private static WalletTransaction CreateTransaction(
        string id,
        string walletId,
        WalletTransactionType type,
        string key,
        string deliveryId,
        decimal amount,
        DateTime now) => new()
    {
        Id = id,
        WalletId = walletId,
        OperationType = type,
        IdempotencyKey = key,
        Amount = amount,
        RelatedDeliveryId = deliveryId,
        CreatedAtUtc = now
    };

    private static async Task InsertInteractionAsync(
        MediBridgeDbContext context,
        string id,
        string deliveryId,
        string idempotencyHash,
        string fingerprint,
        int feedbackLength)
    {
        var feedback = new string('a', feedbackLength);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [DeliveryInteractions]
                ([Id], [DeliveryId], [DoctorId], [ActorUserId], [Outcome], [IdempotencyKeyHash], [RequestFingerprint], [FeedbackText], [FeedbackQualifiesForScore], [ChargeTransactionId], [EarnTransactionId], [AuditEventId], [CreatedAtUtc])
            VALUES
                ({id}, {deliveryId}, {"doctor-phase8-migration"}, {"phase8-migration-doctor-user"}, {1}, {idempotencyHash}, {fingerprint}, {feedback}, {true}, {"charge-transaction-phase8-migration"}, {"earn-transaction-phase8-migration"}, {"audit-phase8-migration"}, {new DateTime(2026, 7, 11, 10, 5, 0, DateTimeKind.Utc)})
            """);
    }

    private static async Task AssertPhase8SchemaAsync(MediBridgeDbContext context)
    {
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeliveryInteractions'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.check_constraints WHERE [name] = 'CK_DeliveryInteractions_Outcome'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.check_constraints WHERE [name] = 'CK_DeliveryInteractions_Feedback_Length'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DeliveryInteractions_DeliveryId' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DeliveryInteractions_DoctorId_IdempotencyKeyHash' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Status_ReservationStatus_Id'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeliveryJobRuns'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeliveryRecoveryDispatches'"));
        Assert.Equal(3, await ExecuteScalarIntAsync(context, "SELECT NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DoctorAdDeliveries' AND COLUMN_NAME = 'PlatformFeePercentSnapshot'"));
    }

    private static async Task<BusinessCounts> CountBusinessRowsAsync(MediBridgeDbContext context)
    {
        return new BusinessCounts(
            await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM [DoctorAdDeliveries] WHERE [Id] LIKE 'delivery-phase8-migration%'"),
            await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM [WalletTransactions] WHERE [Id] LIKE '%transaction-phase8-migration'"),
            await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM [AuditEvents] WHERE [Id] = 'audit-phase8-migration'"));
    }

    private static async Task<int> ExecuteScalarIntAsync(MediBridgeDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record BusinessCounts(int Deliveries, int Transactions, int Audits);
}

public sealed class Phase8SqlServerMigrationFactAttribute : FactAttribute
{
    public Phase8SqlServerMigrationFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MEDIBRIDGE_PHASE8_SQLSERVER_MIGRATION"), "1", StringComparison.Ordinal))
        {
            Skip = "Set MEDIBRIDGE_PHASE8_SQLSERVER_MIGRATION=1 on a host with a working SQL Server 2022 Testcontainers Docker endpoint to collect full Phase 8 migration constraint evidence.";
        }
    }
}
