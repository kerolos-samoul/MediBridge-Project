using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ProfileActivityScoreHistory = MediBridge.Core.Entities.Profiles.ActivityScoreHistory;

namespace MediBridge.IntegrationTests;

public sealed class Phase9MigrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase9MigrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Phase9Migration_CreatesTablesColumnsIndexesAndPreservesPriorSchema()
    {
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await AssertColumnExistsAsync(context, "DoctorProfiles", "SuspendedAtUtc");
        await AssertColumnExistsAsync(context, "DoctorProfiles", "SuspendedUntilUtc");
        await AssertColumnExistsAsync(context, "DoctorProfiles", "LastStatusChangedAtUtc");
        await AssertTableExistsAsync(context, "DoctorActivityScoreHistories");
        await AssertTableExistsAsync(context, "WeeklyEnforcementDecisions");
        await AssertTableExistsAsync(context, "DoctorWeeklyViolations");
        await AssertTableExistsAsync(context, "DoctorEnforcementActions");
        await AssertTableExistsAsync(context, "ActivityEnforcementJobRuns");
        await AssertIndexExistsAsync(context, "DoctorActivityScoreHistories", "IX_DoctorActivityScoreHistories_DoctorId_ScoreDateEgypt");
        await AssertIndexExistsAsync(context, "WeeklyEnforcementDecisions", "IX_WeeklyEnforcementDecisions_DoctorId_WeekStartDateEgypt");
        await AssertIndexExistsAsync(context, "DoctorWeeklyViolations", "IX_DoctorWeeklyViolations_DoctorId_WeekStartDateEgypt");
        await AssertCheckConstraintExistsAsync(context, "ActivityEnforcementJobRuns", "CK_ActivityEnforcementJobRuns_Counters_NonNegative");
        await AssertTableExistsAsync(context, "Wallets");
        await AssertTableExistsAsync(context, "WalletTransactions");
        await AssertTableExistsAsync(context, "WalletLedgerEntries");
        await AssertTableExistsAsync(context, "DoctorAdDeliveries");
        await AssertIndexExistsAsync(context, "DoctorAdDeliveries", "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id");
    }

    [Fact]
    public async Task Phase9Migration_RejectsDuplicateSnapshotDecisionAndViolationRows()
    {
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var doctorId = await SeedApprovedDoctorAsync(context);
        var scoreDate = new DateOnly(2026, 7, 12);
        var weekStart = new DateOnly(2026, 7, 6);
        var now = DateTime.UtcNow;

        context.DoctorActivityScoreHistories.Add(CreateScoreSnapshot("score-a", doctorId, scoreDate, now));
        context.DoctorActivityScoreHistories.Add(CreateScoreSnapshot("score-b", doctorId, scoreDate, now));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        context.WeeklyEnforcementDecisions.Add(CreateDecision("decision-a", doctorId, weekStart, now));
        context.WeeklyEnforcementDecisions.Add(CreateDecision("decision-b", doctorId, weekStart, now));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var decision = CreateDecision("decision-c", doctorId, weekStart, now);
        context.WeeklyEnforcementDecisions.Add(decision);
        await context.SaveChangesAsync();
        context.DoctorWeeklyViolations.Add(CreateViolation("violation-a", doctorId, decision.Id, weekStart, now));
        context.DoctorWeeklyViolations.Add(CreateViolation("violation-b", doctorId, decision.Id, weekStart, now));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Phase9Migration_RejectsNegativeJobCounters()
    {
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ActivityEnforcementJobRuns
                (Id, JobType, StartedAtUtc, Status, ProcessedCount, SkippedCount, CreatedCount, UpdatedCount, FailedCount, CreatedAtUtc)
            VALUES
                (@id, 1, SYSUTCDATETIME(), 1, -1, 0, 0, 0, 0, SYSUTCDATETIME())
            """;
        command.Parameters.AddWithValue("@id", $"phase9-job-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private static ProfileActivityScoreHistory CreateScoreSnapshot(string id, string doctorId, DateOnly scoreDate, DateTime now)
    {
        return new ProfileActivityScoreHistory
        {
            Id = id,
            DoctorId = doctorId,
            ScoreDateEgypt = scoreDate,
            WindowStartDateEgypt = scoreDate.AddDays(-30),
            WindowEndDateEgypt = scoreDate.AddDays(-1),
            DeliveredCount = 0,
            InteractedCount = 0,
            FeedbackQualifiedCount = 0,
            ResponseSpeedScore = 0m,
            EngagementScore = 0m,
            FeedbackScore = 0m,
            FinalScore = 95m,
            CalculationMode = ActivityScoreCalculationMode.DefaultNoDeliveries,
            CalculatedAtUtc = now,
            CreatedAtUtc = now
        };
    }

    private static WeeklyEnforcementDecision CreateDecision(string id, string doctorId, DateOnly weekStart, DateTime now)
    {
        return new WeeklyEnforcementDecision
        {
            Id = id,
            DoctorId = doctorId,
            WeekStartDateEgypt = weekStart,
            WeekEndDateEgypt = weekStart.AddDays(7),
            MinimumWeeklyRequirement = 5,
            InteractionCount = 3,
            Decision = WeeklyEnforcementDecisionType.Violation,
            CreatedAtUtc = now
        };
    }

    private static DoctorWeeklyViolation CreateViolation(string id, string doctorId, string decisionId, DateOnly weekStart, DateTime now)
    {
        return new DoctorWeeklyViolation
        {
            Id = id,
            DoctorId = doctorId,
            WeeklyEnforcementDecisionId = decisionId,
            WeekStartDateEgypt = weekStart,
            WeekEndDateEgypt = weekStart.AddDays(7),
            MinimumWeeklyRequirement = 5,
            InteractionCount = 3,
            RollingViolationCount = 1,
            CreatedAtUtc = now
        };
    }

    private static async Task<string> SeedApprovedDoctorAsync(MediBridgeDbContext context)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new MediBridgeIdentityUser
        {
            Id = $"phase9-user-{suffix}",
            UserName = $"phase9-{suffix}@medibridge.local",
            NormalizedUserName = $"PHASE9-{suffix}@MEDIBRIDGE.LOCAL",
            Email = $"phase9-{suffix}@medibridge.local",
            NormalizedEmail = $"PHASE9-{suffix}@MEDIBRIDGE.LOCAL",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var doctor = new DoctorProfile
        {
            Id = $"phase9-doctor-{suffix}",
            UserId = user.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"phase9/{suffix}",
            DailyMessageLimit = 10,
            MinimumWeeklyRequirement = 5,
            PricePerMessage = 50m
        };
        await context.Users.AddAsync(user);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.SaveChangesAsync();
        return doctor.Id;
    }

    private static async Task AssertTableExistsAsync(MediBridgeDbContext context, string tableName)
    {
        var count = await ScalarIntAsync(
            context,
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @name",
            tableName);
        Assert.Equal(1, count);
    }

    private static async Task AssertColumnExistsAsync(MediBridgeDbContext context, string tableName, string columnName)
    {
        var count = await ScalarIntAsync(
            context,
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @name AND COLUMN_NAME = @second",
            tableName,
            columnName);
        Assert.Equal(1, count);
    }

    private static async Task AssertIndexExistsAsync(MediBridgeDbContext context, string tableName, string indexName)
    {
        var count = await ScalarIntAsync(
            context,
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(@name) AND name = @second",
            tableName,
            indexName);
        Assert.Equal(1, count);
    }

    private static async Task AssertCheckConstraintExistsAsync(MediBridgeDbContext context, string tableName, string constraintName)
    {
        var count = await ScalarIntAsync(
            context,
            "SELECT COUNT(1) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(@name) AND name = @second",
            tableName,
            constraintName);
        Assert.Equal(1, count);
    }

    private static async Task<int> ScalarIntAsync(MediBridgeDbContext context, string sql, string name, string? second = null)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var nameParameter = command.CreateParameter();
        nameParameter.ParameterName = "@name";
        nameParameter.Value = name;
        command.Parameters.Add(nameParameter);
        if (second is not null)
        {
            var secondParameter = command.CreateParameter();
            secondParameter.ParameterName = "@second";
            secondParameter.Value = second;
            command.Parameters.Add(secondParameter);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
