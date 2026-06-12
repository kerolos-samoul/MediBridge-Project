using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxAttemptsReachedColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'FailedAttemptCount')
                ALTER TABLE [ContactVerificationFlows] ADD [FailedAttemptCount] int NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'LastSentAtUtc')
                ALTER TABLE [ContactVerificationFlows] ADD [LastSentAtUtc] datetime2 NOT NULL DEFAULT SYSUTCDATETIME();
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'MaxAttemptsReachedAtUtc')
                ALTER TABLE [ContactVerificationFlows] ADD [MaxAttemptsReachedAtUtc] datetime2 NULL;
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'SupersededAtUtc')
                ALTER TABLE [ContactVerificationFlows] ADD [SupersededAtUtc] datetime2 NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'FailedAttemptCount')
                ALTER TABLE [ContactVerificationFlows] DROP COLUMN [FailedAttemptCount];
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'LastSentAtUtc')
                ALTER TABLE [ContactVerificationFlows] DROP COLUMN [LastSentAtUtc];
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'MaxAttemptsReachedAtUtc')
                ALTER TABLE [ContactVerificationFlows] DROP COLUMN [MaxAttemptsReachedAtUtc];
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactVerificationFlows') AND name = 'SupersededAtUtc')
                ALTER TABLE [ContactVerificationFlows] DROP COLUMN [SupersededAtUtc];
                """);
        }
    }
}
